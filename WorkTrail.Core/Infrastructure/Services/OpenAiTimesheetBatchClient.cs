// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Uses the real OpenAI Batch endpoint; paid POSTs are never automatically retried.</summary>
internal sealed class OpenAiTimesheetBatchClient(string apiKey, HttpClient? transport = null)
{
    private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
    private HttpClient Transport => transport ?? Shared;

    // rows are already bounded and contain only the user's selected text sources.
    // token cancels transport, not a previously accepted cloud batch.
    internal async Task<string> UploadAsync(IReadOnlyList<TimesheetWorkRow> rows, CancellationToken token)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("batch"), "purpose");
        var lines = rows.Where(row => row.Prompt is not null).Select(row => JsonSerializer.Serialize(new
        {
            custom_id = row.Row.Id,
            method = "POST",
            url = "/v1/responses",
            body = new
            {
                model = ReportSummaryModelPolicy.OpenAiModel,
                input = row.Prompt,
                store = false,
                reasoning = new { effort = "none" },
                max_output_tokens = 1_000
            }
        }));
        content.Add(new StringContent(string.Join('\n', lines) + "\n", Encoding.UTF8, "application/jsonl"), "file", "timesheet.jsonl");
        using var result = await JsonAsync(HttpMethod.Post, "files", content, token).ConfigureAwait(false);
        return Id(result.RootElement.GetProperty("id").GetString());
    }

    // inputFile identifies the already uploaded request file.
    // jobId enables reconciliation if a create response is lost.
    // token cancels only the local request.
    internal Task<JsonDocument> CreateAsync(string inputFile, Guid jobId, CancellationToken token) =>
        JsonAsync(HttpMethod.Post, "batches", JsonContent(new
        {
            input_file_id = Id(inputFile),
            endpoint = "/v1/responses",
            completion_window = "24h",
            metadata = new { worktrail_job = jobId.ToString("N") }
        }), token);

    // batch identifies the durable remote job.
    // token cancels status retrieval.
    internal Task<JsonDocument> GetAsync(string batch, CancellationToken token) => JsonAsync(HttpMethod.Get, "batches/" + Id(batch), null, token);

    // batch identifies the user's explicit cancellation target.
    // token cancels local transport; OpenAI cancellation can remain pending.
    internal Task<JsonDocument> CancelAsync(string batch, CancellationToken token) => JsonAsync(HttpMethod.Post, "batches/" + Id(batch) + "/cancel", null, token);

    // jobId matches only this app's persisted submission, never a guessed recent job.
    // token cancels the bounded recovery scan.
    internal async Task<JsonDocument?> FindAsync(Guid jobId, CancellationToken token)
    {
        string? after = null;
        for (var page = 0; page < 20; page++)
        {
            using var response = await JsonAsync(HttpMethod.Get, "batches?limit=100" + (after is null ? "" : "&after=" + Id(after)), null, token).ConfigureAwait(false);
            foreach (var batch in response.RootElement.GetProperty("data").EnumerateArray())
            {
                if (batch.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                    && metadata.TryGetProperty("worktrail_job", out var value) && value.GetString() == jobId.ToString("N"))
                    return JsonDocument.Parse(batch.GetRawText());
            }
            if (!response.RootElement.GetProperty("has_more").GetBoolean()) break;
            after = response.RootElement.GetProperty("last_id").GetString();
            if (string.IsNullOrEmpty(after)) throw new InvalidDataException("Missing batch pagination cursor.");
        }
        return null;
    }

    // file is a provider identifier retained in the job snapshot.
    // token cancels a bounded output download.
    internal Task<string> DownloadAsync(string file, CancellationToken token) => SendAsync(HttpMethod.Get, "files/" + Id(file) + "/content", null, 64_000_000, token);

    // file is deleted only after results are durably saved or cancellation has completed.
    // token cancels cleanup; the identifier remains saved for a later retry.
    internal Task<string> DeleteAsync(string file, CancellationToken token) => SendAsync(HttpMethod.Delete, "files/" + Id(file), null, 1_000_000, token);

    // value contains protocol fields, never credentials.
    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    // method and path select an allowlisted endpoint under the fixed OpenAI host.
    // content contains the explicit API payload.
    // token cancels transport.
    private async Task<JsonDocument> JsonAsync(HttpMethod method, string path, HttpContent? content, CancellationToken token) =>
        JsonDocument.Parse(await SendAsync(method, path, content, 4_000_000, token).ConfigureAwait(false));

    // method selects transport semantics without retrying paid submissions.
    // path is constructed only from fixed routes and validated IDs.
    // content contains selected text; limit bounds remote response memory.
    // token cancels the local read.
    private async Task<string> SendAsync(HttpMethod method, string path, HttpContent? content, int limit, CancellationToken token)
    {
        // ResponseHeadersRead ends HttpClient's timeout at the headers. Bound the entire streamed response as well.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        token = deadline.Token;
        using var request = new HttpRequestMessage(method, "https://api.openai.com/v1/" + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await Transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (method == HttpMethod.Delete && response.StatusCode == System.Net.HttpStatusCode.NotFound) return "{}";
        if (!response.IsSuccessStatusCode)
        {
            var messageKey = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? "Timesheet.Credentials" : "Timesheet.RemoteError";
            // A rejected create request cannot have accepted paid work. Timeouts and server failures remain uncertain.
            if (method == HttpMethod.Post && path == "batches" && (int)response.StatusCode is >= 400 and < 500
                && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout)
                throw new TimesheetSubmissionRejectedException(messageKey);
            throw new ReportExportValidationException(messageKey);
        }
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Batch response exceeds the local limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Batch response exceeds the local limit.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    // value is an opaque provider identifier, never a URL or file path.
    private static string Id(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? value : throw new InvalidDataException("Invalid provider identifier.");
}

// A definite create rejection is distinct from a lost response to an accepted submission.
internal sealed class TimesheetSubmissionRejectedException(string messageKey) : Exception(messageKey)
{
    internal string MessageKey { get; } = messageKey;
}
