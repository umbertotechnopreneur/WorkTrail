// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Text.Json;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Persists remote job identities and results independently of the export window lifetime.</summary>
internal sealed class TimesheetBatchService(LocalStore store, HttpClient? transport = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int PageSize = 30;
    private readonly Dictionary<Guid, (DateTime LastWrite, long Length, TimesheetJobInfo Info)> _catalog = [];
    private readonly Dictionary<Guid, DateTimeOffset> _recoveryAttempts = [];
    private string DirectoryPath => Path.Combine(store.DataDirectory, "timesheet-batches");

    // command selects one explicit operation.
    // settings supplies current connection metadata, never serialized credentials.
    // reserve checks entitlement and budgets while serializing usage reservations.
    // saveUsage reconciles stable request identities through the application mutation boundary.
    // token cancels local work; accepted cloud jobs remain recoverable.
    internal async Task<TimesheetBatchView> ExecuteAsync(TimesheetBatchCommand command, AppSettings settings,
        Func<TimesheetJob, CancellationToken, Task> reserve,
        Func<TimesheetJob, CancellationToken, Task> saveUsage, CancellationToken token)
    {
        if (!Enum.IsDefined(command.Action) || command.Page < 0 || command.Page > int.MaxValue / PageSize)
            throw new ArgumentException("Invalid timesheet command.");
        if (command.Action == TimesheetBatchAction.Start)
        {
            var options = command.Options ?? throw new ArgumentException("Timesheet options are required.");
            var job = new TimesheetJob
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                Options = options,
                Rows = TimesheetProjection.Build(store, options, token),
                ApiKeyName = settings.AiApiKeyName
            };
            if (!job.Rows.Any(row => row.Prompt is not null)) throw new ReportExportValidationException("Export.NoSources");
            ValidateProvider(settings, requireEnabled: true);
            var client = Client(job);
            // An unresolved submission must be reconciled before another paid batch can be sent.
            if (List(token).Any(item => item.State == "submitting")) throw new ReportExportValidationException("Timesheet.Unconfirmed");
            Save(job);
            try
            {
                await reserve(job, token).ConfigureAwait(false);
                job.Reserved = true;
                Save(job);
                job.InputFile = await client.UploadAsync(job.Rows, token).ConfigureAwait(false);
                job.State = "submitting";
                Save(job);
                using var response = await client.CreateAsync(job.InputFile, job.Id, token).ConfigureAwait(false);
                ApplyRemote(job, response.RootElement);
                // Persist a successful response even if the local window was closed during transport.
                job.Rows.ForEach(row => row.Prompt = null);
                Save(job);
            }
            catch (Exception exception)
            {
                if (job.State == "prepared" || exception is TimesheetSubmissionRejectedException)
                {
                    job.State = "failed";
                    job.Rows.ForEach(row => row.Row = row.Row with { State = row.Row.State == "pending" ? "failed" : row.Row.State });
                    job.ResultsSaved = true;
                }
                job.Rows.ForEach(row => row.Prompt = null);
                Save(job);
                if (job.ResultsSaved)
                {
                    // Release only known-unsubmitted reservations before the user can retry. No remote work is retried here.
                    await saveUsage(job, CancellationToken.None).ConfigureAwait(false);
                    job.UsageSaved = true;
                    Save(job);
                }
                if (exception is TimesheetSubmissionRejectedException rejected)
                    throw new ReportExportValidationException(rejected.MessageKey);
                throw;
            }
            return View(Info(job), 0, token);
        }
        if (command.Action == TimesheetBatchAction.List)
        {
            var jobs = List(token);
            var page = Math.Min(command.Page, jobs.Count == 0 ? 0 : (jobs.Count - 1) / PageSize);
            var offset = page * PageSize;
            var selected = command.JobId is { } id
                ? jobs.SingleOrDefault(job => job.Id == id) ?? throw new ArgumentException("The saved timesheet job does not exist.")
                : offset < jobs.Count ? jobs[offset] : null;
            return View(selected, page, token);
        }
        var current = Load(command.JobId ?? throw new ArgumentException("A saved timesheet job is required."));
        if (command.Action == TimesheetBatchAction.Export)
        {
            if (!current.ResultsSaved) throw new ReportExportValidationException("Timesheet.NotReady");
            var path = TimesheetExcelWriter.Write(current, command.DestinationPath ?? "", command.Overwrite, token);
            return View(Info(current), command.Page, token) with { ExportedPath = path };
        }
        // Saved jobs belong to their original OpenAI connection, even after the selected provider changes.
        OpenAiTimesheetBatchClient? api = null;
        OpenAiTimesheetBatchClient Api() => api ??= Client(current);
        if (current.State == "submitting" && current.BatchId is null)
        {
            using var recovered = await Api().FindAsync(current.Id, token).ConfigureAwait(false);
            if (recovered is null) throw new ReportExportValidationException("Timesheet.Unconfirmed");
            ApplyRemote(current, recovered.RootElement);
            Save(current);
        }
        if (current.BatchId is null)
        {
            current.State = "failed";
            current.Rows.ForEach(row => row.Row = row.Row with { State = row.Row.State == "pending" ? "failed" : row.Row.State });
            current.ResultsSaved = true;
            Save(current);
        }
        else if (!current.ResultsSaved)
        {
            using var response = command.Action == TimesheetBatchAction.Cancel && !Terminal(current.State)
                ? await Api().CancelAsync(current.BatchId, token).ConfigureAwait(false)
                : await Api().GetAsync(current.BatchId, token).ConfigureAwait(false);
            ApplyRemote(current, response.RootElement);
            Save(current);
            if (Terminal(current.State))
            {
                var output = current.OutputFile is null ? "" : await Api().DownloadAsync(current.OutputFile, token).ConfigureAwait(false);
                var errors = current.ErrorFile is null ? "" : await Api().DownloadAsync(current.ErrorFile, token).ConfigureAwait(false);
                ReadResults(current, output, errors);
                current.ResultsSaved = true;
                Save(current);
            }
        }
        if (current.ResultsSaved)
        {
            try
            {
                // Stable telemetry IDs make replay after a crash safe, including a crash before this callback.
                if (!current.UsageSaved)
                {
                    await saveUsage(current, token).ConfigureAwait(false);
                    current.UsageSaved = true;
                    Save(current);
                }
                foreach (var file in new[] { current.InputFile, current.OutputFile, current.ErrorFile }.OfType<string>().Distinct())
                {
                    if (current.DeletedFiles.Contains(file)) continue;
                    // Record each successful deletion; failed cleanup remains visible and retryable.
                    await Api().DeleteAsync(file, token).ConfigureAwait(false);
                    current.DeletedFiles.Add(file);
                    Save(current);
                }
            }
            catch (ReportExportValidationException exception)
            {
                // Durable results remain exportable while cloud cleanup is visibly pending and retryable.
                return View(Info(current), command.Page, token) with { WarningKey = exception.MessageKey };
            }
        }
        return View(Info(current), command.Page, token);
    }

    // settings determines whether this connection is an actual OpenAI API connection.
    // requireEnabled blocks new work after AI has been disabled while allowing explicit cancellation.
    internal static void ValidateProvider(AppSettings settings, bool requireEnabled)
    {
        if ((requireEnabled && !settings.OpenAiEnabled)
            || settings.AiProvider is not ("openai" or "open-ai")
            || !Uri.TryCreate(settings.AiEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "https" || endpoint.Host != "api.openai.com" || !endpoint.IsDefaultPort)
            throw new ReportExportValidationException("Timesheet.OpenAiRequired");
    }

    // job retains only the configured key name; its secret is resolved at the point of use.
    private OpenAiTimesheetBatchClient Client(TimesheetJob job) => new(
        store.LoadApiKey(job.ApiKeyName) is { Length: > 0 } key ? key : throw new ReportExportValidationException("Timesheet.Credentials"), transport);

    // job receives only validated metadata from the remote response.
    // remote is the batch object, never its free-form error text.
    private static void ApplyRemote(TimesheetJob job, JsonElement remote)
    {
        var id = remote.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing batch ID.");
        if (job.BatchId is not null && job.BatchId != id) throw new InvalidDataException("Batch identity changed.");
        var state = remote.GetProperty("status").GetString() ?? "";
        if (state is not ("validating" or "in_progress" or "finalizing" or "completed" or "failed" or "expired" or "cancelling" or "cancelled"))
            throw new InvalidDataException("Unknown batch state.");
        job.BatchId = id;
        job.State = state;
        job.OutputFile = remote.TryGetProperty("output_file_id", out var output) ? output.GetString() : null;
        job.ErrorFile = remote.TryGetProperty("error_file_id", out var error) ? error.GetString() : null;
    }

    // job defines the expected custom IDs; output order is deliberately irrelevant.
    // output contains successful or failed Responses envelopes.
    // errors contains provider failures; free-form provider messages are not displayed or persisted.
    internal static void ReadResults(TimesheetJob job, string output, string errors)
    {
        var expected = job.Rows.Where(row => row.Row.State != "no_sources").ToDictionary(row => row.Row.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in (output + "\n" + errors).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var record = document.RootElement;
            var id = record.GetProperty("custom_id").GetString() ?? "";
            if (!expected.TryGetValue(id, out var row) || !seen.Add(id)) throw new InvalidDataException("Unexpected or duplicate batch row.");
            var text = "";
            var valid = false;
            if (record.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    row.Usage = new(InputTokens: AiProviderTelemetry.ReadLong(usage, "input_tokens"),
                        OutputTokens: AiProviderTelemetry.ReadLong(usage, "output_tokens"), TotalTokens: AiProviderTelemetry.ReadLong(usage, "total_tokens"),
                        CachedInputTokens: usage.TryGetProperty("input_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object
                            ? AiProviderTelemetry.ReadLong(details, "cached_tokens") : null);
                row.ResponseId = body.TryGetProperty("id", out var responseId) ? responseId.GetString() : null;
                if (response.GetProperty("status_code").GetInt32() == 200
                    && body.TryGetProperty("status", out var status) && status.GetString() == "completed"
                    && body.TryGetProperty("output", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    var refused = false;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                        foreach (var part in content.EnumerateArray())
                        {
                            var type = part.GetProperty("type").GetString();
                            if (type == "refusal") refused = true;
                            if (type == "output_text") parts.Add(part.GetProperty("text").GetString() ?? "");
                        }
                    }
                    text = string.Join("\n", parts).Trim();
                    valid = !refused && text.Length is > 0 and <= 6_000;
                }
            }
            row.Row = row.Row with { Description = valid ? text : "", State = valid ? "completed" : "failed" };
        }
        foreach (var row in expected.Values.Where(row => !seen.Contains(row.Row.Id)))
            row.Row = row.Row with { State = "failed", Description = "" };
    }

    // selected is cached safe metadata, independent of which archive page is displayed.
    // page selects thirty metadata entries without putting report rows on IPC.
    // token cancels catalog enumeration before returning a page.
    private TimesheetBatchView View(TimesheetJobInfo? selected, int page, CancellationToken token)
    {
        var jobs = List(token);
        var lastPage = jobs.Count == 0 ? 0 : (jobs.Count - 1) / PageSize;
        page = Math.Min(page, lastPage);
        return new(jobs.Skip(page * PageSize).Take(PageSize).ToArray(), selected,
            Page: page, HasPreviousPage: page > 0, HasNextPage: page < lastPage);
    }

    // token cancels a bounded recovery selection; oldest attempted jobs rotate fairly after errors.
    internal IReadOnlyList<Guid> RecoveryCandidates(CancellationToken token)
    {
        var selected = List(token).Where(job => !job.ResultsSaved || job.CleanupPending)
            .OrderBy(job => _recoveryAttempts.GetValueOrDefault(job.Id))
            .ThenBy(job => job.CreatedAt).Take(8).Select(job => job.Id).ToArray();
        foreach (var id in selected) _recoveryAttempts[id] = DateTimeOffset.UtcNow;
        return selected;
    }

    // job contains measured rows and remote status, never credentials.
    private static TimesheetJobInfo Info(TimesheetJob job) => new(job.Id, job.CreatedAt, job.Options.Sources.Options.From,
        job.Options.Sources.Options.ToInclusive, job.State, job.Rows.Count, job.Rows.Count(row => row.Row.State == "completed"),
        job.Rows.Count(row => row.Row.State is "failed" or "no_sources"), job.ResultsSaved,
        !job.UsageSaved || new[] { job.InputFile, job.OutputFile, job.ErrorFile }
            .OfType<string>().Any(file => !job.DeletedFiles.Contains(file)));

    // state is normalized from the documented OpenAI Batch state machine.
    internal static bool Terminal(string state) => state is "completed" or "failed" or "expired" or "cancelled";

    // id is a GUID, preventing user-controlled paths from escaping the owned job directory.
    private string JobPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".json");

    // id identifies the durable snapshot; unsupported or corrupt data fails visibly.
    internal TimesheetJob Load(Guid id)
    {
        var path = JobPath(id);
        if (new FileInfo(path).Length > 64_000_000) throw new InvalidDataException("Timesheet snapshot is too large.");
        var job = JsonSerializer.Deserialize<TimesheetJob>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Missing timesheet snapshot.");
        if (job.Version != 1 || job.Id != id || job.Rows.Count > TimesheetProjection.MaximumRows) throw new InvalidDataException("Invalid timesheet snapshot.");
        return job;
    }

    // token cancels archive reads; cached metadata avoids repeatedly deserializing unchanged report rows.
    private List<TimesheetJobInfo> List(CancellationToken token)
    {
        if (!Directory.Exists(DirectoryPath)) { _catalog.Clear(); _recoveryAttempts.Clear(); return []; }
        var present = new HashSet<Guid>();
        var jobs = new List<TimesheetJobInfo>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            var id = Guid.ParseExact(Path.GetFileNameWithoutExtension(path), "N");
            present.Add(id);
            var file = new FileInfo(path);
            if (!_catalog.TryGetValue(id, out var cached) || cached.LastWrite != file.LastWriteTimeUtc || cached.Length != file.Length)
            {
                cached = (file.LastWriteTimeUtc, file.Length, Info(Load(id)));
                _catalog[id] = cached;
            }
            jobs.Add(cached.Info);
        }
        foreach (var id in _catalog.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            _catalog.Remove(id);
            _recoveryAttempts.Remove(id);
        }
        return jobs.OrderByDescending(job => job.CreatedAt).ThenBy(job => job.Id).ToList();
    }

    // job is atomically replaced so transport interruption cannot corrupt the recovery identity.
    private void Save(TimesheetJob job)
    {
        Directory.CreateDirectory(DirectoryPath);
        ReportExportWriter.AtomicWrite(JobPath(job.Id), true, stream => JsonSerializer.Serialize(stream, job, Json), CancellationToken.None);
        var file = new FileInfo(JobPath(job.Id));
        _catalog[job.Id] = (file.LastWriteTimeUtc, file.Length, Info(job));
    }
}

internal sealed class TimesheetJob
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required TimesheetOptions Options { get; set; }
    public required List<TimesheetWorkRow> Rows { get; set; }
    public string ApiKeyName { get; set; } = "OPENAI_API_KEY";
    public string State { get; set; } = "prepared";
    public string? BatchId { get; set; }
    public string? InputFile { get; set; }
    public string? OutputFile { get; set; }
    public string? ErrorFile { get; set; }
    public bool ResultsSaved { get; set; }
    public bool Reserved { get; set; }
    public bool UsageSaved { get; set; }
    public List<string> DeletedFiles { get; set; } = [];
}
