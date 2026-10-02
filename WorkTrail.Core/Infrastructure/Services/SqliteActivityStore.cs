// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Persists activity history and privacy-safe AI telemetry in the single local SQLite store.</summary>
internal sealed partial class SqliteActivityStore
{
    internal const string DatabaseFileName = "activity.sqlite3";
    /// <summary>Defines the current persisted SQLite contract, shared with portable archive validation.</summary>
    internal const int SchemaVersion = 12;
    private const long FixedEstimatedRowBytes = 96;

    private static readonly SchemaColumn[] ExpectedActivityColumns =
    [
        new("id", "INTEGER", false, 1),
        new("sample_id", "TEXT", true, 0),
        new("timestamp_utc_ticks", "INTEGER", true, 0),
        new("start_utc_ticks", "INTEGER", true, 0),
        new("timestamp_offset_minutes", "INTEGER", true, 0),
        new("duration_seconds", "INTEGER", true, 0),
        new("state", "TEXT", true, 0),
        new("process_name", "TEXT", true, 0),
        new("application", "TEXT", true, 0),
        new("context", "TEXT", true, 0),
        new("window_title", "TEXT", true, 0),
        new("installation_id", "TEXT", true, 0),
        new("key_presses", "INTEGER", true, 0),
        new("mouse_clicks", "INTEGER", true, 0),
        new("attributes_json", "TEXT", false, 0),
        new("estimated_bytes", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedInstallationProfileColumns =
    [
        new("installation_id", "TEXT", true, 1),
        new("machine_name", "TEXT", true, 0),
        new("friendly_name", "TEXT", true, 0),
        new("color", "TEXT", true, 0),
        new("icon", "TEXT", true, 0),
        new("first_seen_utc_ticks", "INTEGER", true, 0),
        new("updated_utc_ticks", "INTEGER", true, 0),
        new("profile_revision", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedScreenshotCaptureColumns =
    [
        new("capture_id", "TEXT", true, 1),
        new("installation_id", "TEXT", true, 0),
        new("captured_utc_ticks", "INTEGER", true, 0),
        new("origin", "TEXT", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedArchiveImportColumns =
    [
        new("archive_id", "TEXT", true, 1),
        new("archive_fingerprint", "TEXT", true, 0),
        new("imported_utc_ticks", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedStoreMetadataColumns =
    [
        new("key", "TEXT", true, 1),
        new("value", "TEXT", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedSearchChangeColumns =
    [
        new("revision", "INTEGER", false, 1),
        new("kind", "TEXT", true, 0),
        new("entity_id", "TEXT", true, 0),
        new("operation", "TEXT", true, 0)
    ];

    private static readonly string[] ExpectedAiRequestUsageColumns =
    [
        "attempt_id", "correlation_id", "occurred_utc_ticks", "completed_utc_ticks", "origin", "request_kind",
        "provider", "endpoint_host", "requested_model", "returned_model", "provider_response_id", "provider_request_id",
        "http_status", "elapsed_ms", "provider_processing_ms", "image_count", "prompt_characters", "max_output_tokens",
        "input_tokens", "output_tokens", "total_tokens", "cached_input_tokens", "cache_write_tokens",
        "cache_creation_input_tokens", "cache_read_input_tokens", "reasoning_tokens", "thinking_tokens",
        "reported_cost_microusd", "reported_upstream_cost_microusd", "cost_source", "finish_reason", "success", "failure_code"
    ];

    private static readonly string[] ExpectedAiAnalysisResultColumns =
    [
        "correlation_id", "attempt_id", "timestamp_utc_ticks", "snapshot_utc_ticks", "application", "context", "summary",
        "installation_id", "origin", "informational_schedule", "screenshot_paths", "image_count"
    ];

    private static readonly SchemaColumn[] ExpectedScreenshotTextSnapshotColumns =
    [
        new("artifact_identity", "TEXT", true, 1),
        new("capture_id", "TEXT", true, 0),
        new("source_path", "TEXT", true, 0),
        new("extracted_utc_ticks", "INTEGER", true, 0),
        new("snapshot_json", "TEXT", true, 0),
        new("updated_utc_ticks", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedScreenshotIntervalTelemetryColumns =
    [
        new("artifact_identity", "TEXT", true, 1),
        new("capture_id", "TEXT", true, 0),
        new("interval_started_utc_ticks", "INTEGER", true, 0),
        new("captured_utc_ticks", "INTEGER", true, 0),
        new("cpu_usage_percent", "INTEGER", false, 0),
        new("gpu_usage_percent", "INTEGER", false, 0),
        new("updated_utc_ticks", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedCaptureHardwareSnapshotColumns =
    [
        new("capture_id", "TEXT", true, 1),
        new("sampled_utc_ticks", "INTEGER", true, 0),
        new("snapshot_json", "TEXT", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedAiAnalysisArtifactColumns =
    [
        new("artifact_identity", "TEXT", true, 1),
        new("capture_id", "TEXT", true, 0),
        new("correlation_id", "TEXT", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedAiReprocessJobColumns =
    [
        new("job_id", "TEXT", true, 1),
        new("created_utc_ticks", "INTEGER", true, 0),
        new("updated_utc_ticks", "INTEGER", true, 0),
        new("range_start_utc_ticks", "INTEGER", true, 0),
        new("range_end_utc_ticks", "INTEGER", true, 0),
        new("selected_local_date", "TEXT", true, 0),
        new("capture_origin", "TEXT", false, 0),
        new("configuration_fingerprint", "TEXT", true, 0),
        new("state", "TEXT", true, 0),
        new("active_slot", "INTEGER", false, 0),
        new("total_captures", "INTEGER", true, 0),
        new("total_screenshots", "INTEGER", true, 0),
        new("pause_reason", "TEXT", false, 0)
    ];

    private static readonly SchemaColumn[] ExpectedAiReprocessJobItemColumns =
    [
        new("job_id", "TEXT", true, 1),
        new("capture_id", "TEXT", true, 2),
        new("ordinal", "INTEGER", true, 0),
        new("captured_utc_ticks", "INTEGER", true, 0),
        new("capture_origin", "TEXT", true, 0),
        new("artifact_identities_json", "TEXT", true, 0),
        new("screenshot_count", "INTEGER", true, 0),
        new("state", "TEXT", true, 0),
        new("attempt_count", "INTEGER", true, 0),
        new("last_code", "TEXT", false, 0),
        new("updated_utc_ticks", "INTEGER", true, 0)
    ];

    private static readonly SchemaColumn[] ExpectedAiModelPricingColumns =
    [
        new("provider", "TEXT", true, 1),
        new("model", "TEXT", true, 2),
        new("service_tier", "TEXT", true, 3),
        new("context_window", "TEXT", true, 4),
        new("currency", "TEXT", true, 0),
        new("input_microusd_per_million", "INTEGER", true, 0),
        new("cached_input_microusd_per_million", "INTEGER", false, 0),
        new("cache_write_microusd_per_million", "INTEGER", false, 0),
        new("output_microusd_per_million", "INTEGER", true, 0),
        new("source_url", "TEXT", true, 0),
        new("source_retrieved_utc_ticks", "INTEGER", true, 0)
    ];

    private static readonly HashSet<string> ExpectedActivityIndexes =
    [
        "ix_activity_samples_start",
        "ix_activity_samples_timestamp",
        "sqlite_autoindex_activity_samples_1"
    ];

    private static readonly HashSet<string> ExpectedAiRequestUsageIndexes =
    [
        "sqlite_autoindex_ai_request_usage_1",
        "ix_ai_request_usage_occurred",
        "ix_ai_request_usage_correlation"
    ];

    private static readonly HashSet<string> ExpectedAiAnalysisResultIndexes =
    [
        "sqlite_autoindex_ai_analysis_results_1",
        "sqlite_autoindex_ai_analysis_results_2",
        "ix_ai_analysis_results_timestamp"
    ];

    private static readonly HashSet<string> ExpectedScreenshotTextSnapshotIndexes =
    [
        "sqlite_autoindex_screenshot_text_snapshots_1",
        "ix_screenshot_text_snapshots_capture"
    ];

    private static readonly HashSet<string> ExpectedScreenshotIntervalTelemetryIndexes =
    [
        "sqlite_autoindex_screenshot_interval_telemetry_1",
        "ix_screenshot_interval_telemetry_capture",
        "ix_screenshot_interval_telemetry_captured"
    ];

    private static readonly HashSet<string> ExpectedAiAnalysisArtifactIndexes =
    [
        "sqlite_autoindex_ai_analysis_artifacts_1",
        "ix_ai_analysis_artifacts_capture"
    ];

    private static readonly HashSet<string> ExpectedAiReprocessJobIndexes =
    [
        "sqlite_autoindex_ai_reprocess_jobs_1",
        "ux_ai_reprocess_jobs_active_slot"
    ];

    private static readonly HashSet<string> ExpectedAiReprocessJobItemIndexes =
    [
        "sqlite_autoindex_ai_reprocess_job_items_1",
        "sqlite_autoindex_ai_reprocess_job_items_2",
        "ix_ai_reprocess_job_items_next"
    ];

    private static readonly HashSet<string> ExpectedAiModelPricingIndexes =
    [
        "sqlite_autoindex_ai_model_pricing_1"
    ];

    private static readonly HashSet<string> ExpectedApplicationSchemaObjects =
    [
        "activity_samples",
        "ix_activity_samples_start",
        "ix_activity_samples_timestamp",
        "ai_request_usage",
        "ix_ai_request_usage_occurred",
        "ix_ai_request_usage_correlation",
        "ai_analysis_results",
        "ix_ai_analysis_results_timestamp",
        "ai_analysis_search",
        "ai_analysis_search_data",
        "ai_analysis_search_idx",
        "ai_analysis_search_content",
        "ai_analysis_search_docsize",
        "ai_analysis_search_config",
        "screenshot_text_snapshots",
        "ix_screenshot_text_snapshots_capture",
        "ai_model_pricing",
        "screenshot_interval_telemetry",
        "ix_screenshot_interval_telemetry_capture",
        "ix_screenshot_interval_telemetry_captured",
        "ai_analysis_artifacts",
        "ix_ai_analysis_artifacts_capture",
        "ai_reprocess_jobs",
        "ux_ai_reprocess_jobs_active_slot",
        "ai_reprocess_job_items",
        "ix_ai_reprocess_job_items_next",
        "installation_profiles",
        "screenshot_captures",
        "ix_screenshot_captures_installation",
        "ix_screenshot_captures_captured",
        "archive_imports",
        "store_metadata",
        "search_change_log",
        "tr_search_activity_insert",
        "tr_search_activity_update",
        "tr_search_activity_delete",
        "tr_search_capture_insert",
        "tr_search_capture_update",
        "tr_search_capture_delete",
        "tr_search_text_insert",
        "tr_search_text_update",
        "tr_search_text_delete",
        "tr_search_telemetry_insert",
        "tr_search_telemetry_update",
        "tr_search_telemetry_delete",
        "tr_search_analysis_insert",
        "tr_search_analysis_update",
        "tr_search_analysis_delete",
        "tr_search_analysis_artifact_insert",
        "tr_search_analysis_artifact_update",
        "tr_search_analysis_artifact_delete",
        "tr_search_profile_insert",
        "tr_search_profile_update",
        "tr_search_profile_delete",
        "capture_hardware_snapshots",
        "ix_capture_hardware_snapshots_sampled",
        "calendar_dataset",
        "calendar_countries",
        "calendar_holidays",
        "ix_calendar_holidays_date",
        "calendar_saints",
        "ix_calendar_saints_month_day"
    ];

    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connections;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions HardwareSnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Initializes and validates the only supported activity-history schema.</summary>
    internal SqliteActivityStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        var databaseExisted = File.Exists(_databasePath);
        _connections = new SqliteConnectionFactory(_databasePath);

        InitializeSchema(databaseExisted);
    }

    /// <summary>Returns the latest durable revision of sources that feed the derived search index.</summary>
    internal long GetSearchSourceRevision()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(revision), 0) FROM search_change_log;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Loads an ordered bounded change range after the supplied committed revision.</summary>
    internal IReadOnlyList<SearchSourceChange> LoadSearchSourceChanges(long afterRevision, int limit)
    {
        if (afterRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterRevision));
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision, kind, entity_id, operation
            FROM search_change_log
            WHERE revision > $after
            ORDER BY revision
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", afterRevision);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var changes = new List<SearchSourceChange>();
        while (reader.Read())
        {
            changes.Add(new SearchSourceChange(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return changes;
    }

    /// <summary>Marks a supported non-SQLite source change that requires a deterministic rebuild.</summary>
    internal long MarkSearchSourceRebuild(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var connection = OpenConnection();
        return MarkSearchSourceRebuild(connection, null, reason);
    }

    /// <summary>Writes an index invalidation in the same transaction as an external history mutation.</summary>
    internal static long MarkSearchSourceRebuild(SqliteConnection connection, SqliteTransaction? transaction, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO search_change_log (kind, entity_id, operation)
            VALUES ('rebuild', $reason, 'upsert');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$reason", reason.Trim());
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Prunes changes covered by a committed derived-index checkpoint.</summary>
    internal int PruneSearchSourceChanges(long throughRevision)
    {
        if (throughRevision <= 0)
        {
            return 0;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM search_change_log WHERE revision < $revision;";
        command.Parameters.AddWithValue("$revision", throughRevision);
        return command.ExecuteNonQuery();
    }

    /// <summary>Gets the absolute path of the SQLite activity database.</summary>
    internal string DatabasePath => _databasePath;

    /// <summary>Creates or refreshes the profile tied to the runtime installation without changing its identity.</summary>
    internal InstallationProfile EnsureCurrentInstallationProfile(InstallationProfile requested)
    {
        var profile = InstallationProfileCatalog.ValidatePersisted(requested);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = LoadInstallationProfile(connection, transaction, profile.InstallationId);
        InstallationProfile persisted;
        if (existing is null)
        {
            InsertInstallationProfile(connection, transaction, profile);
            persisted = profile;
        }
        else if (!string.Equals(existing.MachineName, profile.MachineName, StringComparison.Ordinal))
        {
            var updated = existing with
            {
                MachineName = profile.MachineName,
                FriendlyName = string.Equals(existing.FriendlyName, existing.MachineName, StringComparison.Ordinal)
                    ? profile.MachineName
                    : existing.FriendlyName,
                UpdatedAt = profile.UpdatedAt,
                Revision = checked(existing.Revision + 1)
            };
            UpdateInstallationProfile(connection, transaction, updated, existing.Revision);
            persisted = updated;
        }
        else
        {
            persisted = existing;
        }

        transaction.Commit();
        return persisted with { IsCurrent = true };
    }

    /// <summary>Loads the earliest durable timestamp attributable to one installation, when history exists.</summary>
    internal DateTimeOffset? LoadEarliestInstallationHistoryTimestamp(string installationId)
    {
        if (!Guid.TryParseExact(installationId, "N", out var parsedInstallationId))
        {
            throw new InvalidDataException("The installation identifier is invalid.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MIN(history_utc_ticks)
            FROM (
                SELECT MIN(start_utc_ticks) AS history_utc_ticks
                FROM activity_samples
                WHERE installation_id = $installationId

                UNION ALL

                SELECT MIN(timestamp_utc_ticks) AS history_utc_ticks
                FROM ai_analysis_results
                WHERE installation_id = $installationId

                UNION ALL

                SELECT MIN(captured_utc_ticks) AS history_utc_ticks
                FROM screenshot_captures
                WHERE installation_id = $installationId

            )
            WHERE history_utc_ticks IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$installationId", parsedInstallationId.ToString("N"));
        var value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : new DateTimeOffset(Convert.ToInt64(value, CultureInfo.InvariantCulture), TimeSpan.Zero);
    }

    /// <summary>Lists all known installation profiles and marks only the runtime installation as current.</summary>
    internal IReadOnlyList<InstallationProfile> ListInstallationProfiles(string currentInstallationId)
    {
        if (!Guid.TryParseExact(currentInstallationId, "N", out var currentId))
        {
            throw new InvalidDataException("The current installation identity is invalid.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT installation_id, machine_name, friendly_name, color, icon,
                   first_seen_utc_ticks, updated_utc_ticks, profile_revision
            FROM installation_profiles
            ORDER BY friendly_name COLLATE NOCASE, installation_id;
            """;
        var profiles = new List<InstallationProfile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var profile = ReadInstallationProfile(reader);
            profiles.Add(profile with
            {
                IsCurrent = string.Equals(profile.InstallationId, currentId.ToString("N"), StringComparison.Ordinal)
            });
        }

        return profiles;
    }

    /// <summary>Loads one installation profile, returning null for an unknown identity.</summary>
    internal InstallationProfile? LoadInstallationProfile(string installationId, string currentInstallationId)
    {
        if (!Guid.TryParseExact(installationId, "N", out var parsed)
            || !Guid.TryParseExact(currentInstallationId, "N", out var current))
        {
            throw new InvalidDataException("The installation identity is invalid.");
        }

        using var connection = OpenConnection();
        var profile = LoadInstallationProfile(connection, null, parsed.ToString("N"));
        return profile is null
            ? null
            : profile with { IsCurrent = parsed == current };
    }

    /// <summary>Persists a validated optimistic profile revision.</summary>
    internal InstallationProfile SaveInstallationProfile(InstallationProfile profile, long previousRevision)
    {
        var validated = InstallationProfileCatalog.ValidatePersisted(profile);
        if (previousRevision < 1 || validated.Revision != checked(previousRevision + 1))
        {
            throw new InvalidOperationException("The installation profile revision is invalid.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpdateInstallationProfile(connection, transaction, validated, previousRevision);
        transaction.Commit();
        return validated;
    }

    /// <summary>Registers the immutable owner and acquisition facts of one retained screenshot capture.</summary>
    internal void RegisterScreenshotCapture(
        string captureId,
        string installationId,
        DateTimeOffset capturedAt,
        string origin)
    {
        using var connection = OpenConnection();
        RegisterScreenshotCapture(connection, null, captureId, installationId, capturedAt, origin);
    }

    private static void RegisterScreenshotCapture(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string captureId,
        string installationId,
        DateTimeOffset capturedAt,
        string origin)
    {
        var capture = new ScreenshotCaptureRegistration(captureId, capturedAt, origin).Validate();
        if (!Guid.TryParseExact(installationId, "N", out var parsedInstallation))
        {
            throw new InvalidDataException("Screenshot capture provenance contains an invalid identifier.");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO screenshot_captures (capture_id, installation_id, captured_utc_ticks, origin)
            VALUES ($captureId, $installationId, $capturedAt, $origin)
            ON CONFLICT(capture_id) DO UPDATE SET
                installation_id = excluded.installation_id,
                captured_utc_ticks = excluded.captured_utc_ticks,
                origin = excluded.origin
            WHERE screenshot_captures.installation_id = excluded.installation_id
              AND screenshot_captures.captured_utc_ticks = excluded.captured_utc_ticks
              AND screenshot_captures.origin = excluded.origin;
            """;
        command.Parameters.AddWithValue("$captureId", capture.CaptureId);
        command.Parameters.AddWithValue("$installationId", parsedInstallation.ToString("N"));
        command.Parameters.AddWithValue("$capturedAt", capture.CapturedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$origin", capture.Origin);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException("Screenshot capture provenance conflicts with an existing capture.");
        }
    }

    /// <summary>Returns screenshot capture provenance for the requested capture identifiers.</summary>
    internal IReadOnlyDictionary<string, ScreenshotCaptureProvenance> LoadScreenshotCaptures(
        IEnumerable<string> captureIds)
    {
        ArgumentNullException.ThrowIfNull(captureIds);
        var ids = captureIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => Guid.TryParseExact(id, "N", out var parsed)
                ? parsed.ToString("N")
                : throw new InvalidDataException("Screenshot capture provenance contains an invalid identifier."))
            .ToArray();
        var result = new Dictionary<string, ScreenshotCaptureProvenance>(StringComparer.Ordinal);
        if (ids.Length == 0)
        {
            return result;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var parameters = AddTextParameters(command, ids, "$capture");
        command.CommandText = $"""
            SELECT capture.capture_id, capture.installation_id, capture.captured_utc_ticks, capture.origin,
                   profile.machine_name, profile.friendly_name, profile.color, profile.icon,
                   profile.first_seen_utc_ticks, profile.updated_utc_ticks, profile.profile_revision
            FROM screenshot_captures AS capture
            JOIN installation_profiles AS profile ON profile.installation_id = capture.installation_id
            WHERE capture.capture_id IN ({parameters});
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var capture = new ScreenshotCaptureRegistration(
                reader.GetString(0),
                new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                reader.GetString(3)).Validate();
            var profile = InstallationProfileCatalog.ValidatePersisted(new InstallationProfile(
                reader.GetString(1),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                new DateTimeOffset(reader.GetInt64(8), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(9), TimeSpan.Zero),
                reader.GetInt64(10)));
            var provenance = new ScreenshotCaptureProvenance(
                capture.CaptureId,
                profile,
                capture.CapturedAt,
                capture.Origin);
            result.Add(provenance.CaptureId, provenance);
        }

        return result;
    }

    private static InstallationProfile? LoadInstallationProfile(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string installationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT installation_id, machine_name, friendly_name, color, icon,
                   first_seen_utc_ticks, updated_utc_ticks, profile_revision
            FROM installation_profiles
            WHERE installation_id = $installationId;
            """;
        command.Parameters.AddWithValue("$installationId", installationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var profile = ReadInstallationProfile(reader);
        if (reader.Read())
        {
            throw new InvalidDataException("The installation profile identity is not unique.");
        }

        return profile;
    }

    private static InstallationProfile ReadInstallationProfile(SqliteDataReader reader)
        => InstallationProfileCatalog.ValidatePersisted(new InstallationProfile(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
            new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
            reader.GetInt64(7)));

    private static void InsertInstallationProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InstallationProfile profile)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO installation_profiles (
                installation_id, machine_name, friendly_name, color, icon,
                first_seen_utc_ticks, updated_utc_ticks, profile_revision)
            VALUES (
                $installationId, $machineName, $friendlyName, $color, $icon,
                $firstSeenAt, $updatedAt, $revision);
            """;
        AddInstallationProfileParameters(command, profile);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The installation profile could not be created.");
        }
    }

    private static void UpdateInstallationProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InstallationProfile profile,
        long previousRevision)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE installation_profiles
            SET machine_name = $machineName,
                friendly_name = $friendlyName,
                color = $color,
                icon = $icon,
                first_seen_utc_ticks = $firstSeenAt,
                updated_utc_ticks = $updatedAt,
                profile_revision = $revision
            WHERE installation_id = $installationId
              AND profile_revision = $previousRevision;
            """;
        AddInstallationProfileParameters(command, profile);
        command.Parameters.AddWithValue("$previousRevision", previousRevision);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The installation profile was changed by another operation.");
        }
    }

    private static void AddInstallationProfileParameters(SqliteCommand command, InstallationProfile profile)
    {
        command.Parameters.AddWithValue("$installationId", profile.InstallationId);
        command.Parameters.AddWithValue("$machineName", profile.MachineName);
        command.Parameters.AddWithValue("$friendlyName", profile.FriendlyName);
        command.Parameters.AddWithValue("$color", profile.Color);
        command.Parameters.AddWithValue("$icon", profile.Icon);
        command.Parameters.AddWithValue("$firstSeenAt", profile.FirstSeenAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$updatedAt", profile.UpdatedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$revision", profile.Revision);
    }

    /// <summary>Appends one complete activity sample in a single SQLite statement.</summary>
    internal void Append(ActivitySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.DurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Activity duration must be positive.");
        }

        var attributesJson = sample.Attributes is null ? null : JsonSerializer.Serialize(sample.Attributes, _json);
        var timestampUtcTicks = sample.Timestamp.UtcDateTime.Ticks;
        var durationTicks = checked((long)sample.DurationSeconds * TimeSpan.TicksPerSecond);
        var estimatedBytes = EstimateBytes(sample, attributesJson);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_samples (
                sample_id, timestamp_utc_ticks, start_utc_ticks, timestamp_offset_minutes, duration_seconds, state, process_name,
                application, context, window_title, installation_id, key_presses, mouse_clicks, attributes_json, estimated_bytes)
            VALUES (
                $sampleId, $timestamp, $start, $offset, $duration, $state, $process,
                $application, $context, $windowTitle, $installation, $keyPresses, $mouseClicks, $attributes, $estimatedBytes);
            """;
        command.Parameters.AddWithValue("$sampleId", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$timestamp", timestampUtcTicks);
        command.Parameters.AddWithValue("$start", checked(timestampUtcTicks - durationTicks));
        command.Parameters.AddWithValue("$offset", checked((int)sample.Timestamp.Offset.TotalMinutes));
        command.Parameters.AddWithValue("$duration", sample.DurationSeconds);
        command.Parameters.AddWithValue("$state", sample.State);
        command.Parameters.AddWithValue("$process", sample.ProcessName);
        command.Parameters.AddWithValue("$application", sample.Application);
        command.Parameters.AddWithValue("$context", sample.Context);
        command.Parameters.AddWithValue("$windowTitle", sample.WindowTitle);
        command.Parameters.AddWithValue("$installation", sample.InstallationId);
        command.Parameters.AddWithValue("$keyPresses", sample.KeyPresses);
        command.Parameters.AddWithValue("$mouseClicks", sample.MouseClicks);
        command.Parameters.AddWithValue("$attributes", attributesJson is null ? DBNull.Value : attributesJson);
        command.Parameters.AddWithValue("$estimatedBytes", estimatedBytes);
        command.ExecuteNonQuery();
    }

    /// <summary>Upserts the complete local OCR snapshot and optional AI refinement for one owned screenshot artifact.</summary>
    internal void UpsertScreenshotTextSnapshot(
        string artifactIdentity,
        string captureId,
        ScreenshotTextSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity) || string.IsNullOrWhiteSpace(captureId))
        {
            throw new ArgumentException("Screenshot text persistence requires artifact and capture identifiers.");
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.SourceScreenshotPath)
            || string.IsNullOrWhiteSpace(snapshot.Ocr.Engine)
            || snapshot.Ocr.Lines is null)
        {
            throw new ArgumentException("Screenshot text snapshot is incomplete.", nameof(snapshot));
        }

        var payload = JsonSerializer.Serialize(snapshot, _json);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO screenshot_text_snapshots (
                artifact_identity, capture_id, source_path, extracted_utc_ticks, snapshot_json, updated_utc_ticks)
            VALUES ($identity, $captureId, $sourcePath, $extractedAt, $snapshot, $updatedAt)
            ON CONFLICT(artifact_identity) DO UPDATE SET
                capture_id = excluded.capture_id,
                source_path = excluded.source_path,
                extracted_utc_ticks = excluded.extracted_utc_ticks,
                snapshot_json = excluded.snapshot_json,
                updated_utc_ticks = excluded.updated_utc_ticks;
            """;
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$sourcePath", snapshot.SourceScreenshotPath);
        command.Parameters.AddWithValue("$extractedAt", snapshot.Ocr.ExtractedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$snapshot", payload);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        command.ExecuteNonQuery();
    }

    /// <summary>Stores one immutable capture-time hardware snapshot shared by every display artifact.</summary>
    internal void UpsertCaptureHardwareSnapshot(string captureId, SystemSnapshot snapshot)
    {
        using var connection = OpenConnection();
        UpsertCaptureHardwareSnapshot(connection, null, captureId, snapshot);
    }

    private void UpsertCaptureHardwareSnapshot(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string captureId,
        SystemSnapshot snapshot)
    {
        if (!Guid.TryParseExact(captureId, "N", out _))
        {
            throw new ArgumentException("Hardware telemetry requires a valid capture identifier.", nameof(captureId));
        }

        SystemSnapshotValidator.Validate(snapshot);
        var payload = JsonSerializer.Serialize(snapshot, _json);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // A foreign key requires registered capture provenance. A repeat must be byte-identical;
        // replacing capture-time evidence with a later hardware reading fails explicitly.
        command.CommandText = """
            INSERT INTO capture_hardware_snapshots (capture_id, sampled_utc_ticks, snapshot_json)
            VALUES ($captureId, $sampledAt, $snapshot)
            ON CONFLICT(capture_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$sampledAt", snapshot.Timestamp.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$snapshot", payload);
        if (command.ExecuteNonQuery() == 0)
        {
            command.CommandText = """
                SELECT 1 FROM capture_hardware_snapshots
                WHERE capture_id = $captureId AND sampled_utc_ticks = $sampledAt AND snapshot_json = $snapshot;
                """;
            if (command.ExecuteScalar() is null)
            {
                throw new InvalidDataException("Hardware telemetry already exists for this capture with a different payload.");
            }
        }
    }

    /// <summary>Loads capture-time hardware evidence; older captures legitimately have no reading.</summary>
    internal SystemSnapshot? LoadCaptureHardwareSnapshot(string captureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        return LoadCaptureHardwareSnapshots([captureId], CancellationToken.None).GetValueOrDefault(captureId);
    }

    /// <summary>Loads bounded batches of hardware snapshots for gallery and historical analysis.</summary>
    internal IReadOnlyDictionary<string, SystemSnapshot> LoadCaptureHardwareSnapshots(
        IEnumerable<string> captureIds,
        CancellationToken cancellationToken)
    {
        var identities = captureIds.Distinct(StringComparer.Ordinal).ToArray();
        if (identities.Any(identity => !Guid.TryParseExact(identity, "N", out _)))
        {
            throw new ArgumentException("Hardware telemetry requires valid capture identifiers.", nameof(captureIds));
        }

        var snapshots = new Dictionary<string, SystemSnapshot>(StringComparer.Ordinal);
        using var connection = OpenConnection();
        foreach (var batch in identities.Chunk(400))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            var parameters = AddIdentityParameters(command, batch);
            command.CommandText = $"""
                SELECT capture_id, sampled_utc_ticks, snapshot_json FROM capture_hardware_snapshots
                WHERE capture_id IN ({parameters});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(reader.GetString(0), ReadCaptureHardwareSnapshot(reader.GetInt64(1), reader.GetString(2)));
            }
        }

        return snapshots;
    }

    /// <summary>Validates persisted or imported capture-time hardware JSON before exposing it.</summary>
    internal static SystemSnapshot ReadCaptureHardwareSnapshot(long sampledUtcTicks, string payload)
    {
        var snapshot = JsonSerializer.Deserialize<SystemSnapshot>(payload, HardwareSnapshotJsonOptions)
            ?? throw new InvalidDataException("Persisted capture hardware snapshot is invalid.");
        SystemSnapshotValidator.Validate(snapshot);
        if (snapshot.Timestamp.UtcDateTime.Ticks != sampledUtcTicks)
        {
            throw new InvalidDataException("Persisted hardware snapshot timestamp does not match its metadata.");
        }

        return snapshot;
    }

    /// <summary>Commits provenance, all monitor intervals and optional hardware evidence atomically.</summary>
    internal void UpsertScreenshotCaptureTelemetry(
        string captureId,
        string installationId,
        string origin,
        IReadOnlyList<string> artifactIdentities,
        ScreenshotIntervalTelemetry telemetry,
        SystemSnapshot? hardwareSnapshot)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (artifactIdentities.Count == 0)
        {
            throw new ArgumentException("Capture telemetry requires retained artifact identities.", nameof(artifactIdentities));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        // A failure in any sensor or monitor write rolls back the capture's entire metadata
        // commit. The caller reports storage failure and owns any required image cleanup.
        RegisterScreenshotCapture(connection, transaction, captureId, installationId, telemetry.CapturedAt, origin);
        foreach (var identity in artifactIdentities)
        {
            UpsertScreenshotIntervalTelemetry(connection, transaction, identity, captureId, telemetry);
        }

        if (hardwareSnapshot is not null)
        {
            UpsertCaptureHardwareSnapshot(connection, transaction, captureId, hardwareSnapshot);
        }

        transaction.Commit();
    }

    private static void UpsertScreenshotIntervalTelemetry(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string artifactIdentity,
        string captureId,
        ScreenshotIntervalTelemetry telemetry)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity) || string.IsNullOrWhiteSpace(captureId))
        {
            throw new ArgumentException("Screenshot telemetry persistence requires artifact and capture identifiers.");
        }

        ArgumentNullException.ThrowIfNull(telemetry);
        if (telemetry.IntervalStartedAt >= telemetry.CapturedAt
            || telemetry.CpuUsagePercent is < 0 or > 100
            || telemetry.GpuUsagePercent is < 0 or > 100)
        {
            throw new ArgumentException("Screenshot interval telemetry is invalid.", nameof(telemetry));
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO screenshot_interval_telemetry (
                artifact_identity, capture_id, interval_started_utc_ticks, captured_utc_ticks,
                cpu_usage_percent, gpu_usage_percent, updated_utc_ticks)
            VALUES ($identity, $captureId, $intervalStarted, $captured, $cpu, $gpu, $updated)
            ON CONFLICT(artifact_identity) DO UPDATE SET
                capture_id = excluded.capture_id,
                interval_started_utc_ticks = excluded.interval_started_utc_ticks,
                captured_utc_ticks = excluded.captured_utc_ticks,
                cpu_usage_percent = excluded.cpu_usage_percent,
                gpu_usage_percent = excluded.gpu_usage_percent,
                updated_utc_ticks = excluded.updated_utc_ticks;
            """;
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$intervalStarted", telemetry.IntervalStartedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$captured", telemetry.CapturedAt.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$cpu", telemetry.CpuUsagePercent is { } cpu ? (object)cpu : DBNull.Value);
        command.Parameters.AddWithValue("$gpu", telemetry.GpuUsagePercent is { } gpu ? (object)gpu : DBNull.Value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
        command.ExecuteNonQuery();
    }

    /// <summary>Loads the persisted interval telemetry for one screenshot artifact.</summary>
    internal ScreenshotIntervalTelemetry? LoadScreenshotIntervalTelemetry(string artifactIdentity)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity))
        {
            throw new ArgumentException("Screenshot artifact identity is required.", nameof(artifactIdentity));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT interval_started_utc_ticks, captured_utc_ticks, cpu_usage_percent, gpu_usage_percent
            FROM screenshot_interval_telemetry
            WHERE artifact_identity = $identity;
            """;
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ScreenshotIntervalTelemetry(
                new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
                ReadNullableInt(reader, 2),
                ReadNullableInt(reader, 3))
            : null;
    }

    /// <summary>Loads interval telemetry for a bounded set of screenshot artifact identities.</summary>
    internal IReadOnlyDictionary<string, ScreenshotIntervalTelemetry> LoadScreenshotIntervalTelemetry(
        IEnumerable<string> artifactIdentities,
        CancellationToken cancellationToken)
    {
        var identities = NormalizeArtifactIdentities(artifactIdentities);
        var telemetry = new Dictionary<string, ScreenshotIntervalTelemetry>(StringComparer.OrdinalIgnoreCase);
        if (identities.Length == 0)
        {
            return telemetry;
        }

        using var connection = OpenConnection();
        foreach (var batch in identities.Chunk(400))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            var parameters = AddIdentityParameters(command, batch);
            command.CommandText = $"""
                SELECT artifact_identity, interval_started_utc_ticks, captured_utc_ticks,
                       cpu_usage_percent, gpu_usage_percent
                FROM screenshot_interval_telemetry
                WHERE artifact_identity IN ({parameters});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                telemetry.Add(
                    reader.GetString(0),
                    new ScreenshotIntervalTelemetry(
                        new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
                        new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                        ReadNullableInt(reader, 3),
                        ReadNullableInt(reader, 4)));
            }
        }

        return telemetry;
    }

    /// <summary>Loads the latest persisted screenshot boundary.</summary>
    internal DateTimeOffset? LoadLatestScreenshotTelemetryCapturedAt()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(captured_utc_ticks) FROM screenshot_interval_telemetry;";
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : new DateTimeOffset(Convert.ToInt64(value), TimeSpan.Zero);
    }

    /// <summary>Lists screenshot capture metadata and description state in a half-open UTC interval.</summary>
    internal IReadOnlyList<AiReprocessCatalogRecord> ListScreenshotCapturesForAiReprocessing(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (fromUtc >= toUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(toUtc), "The screenshot reprocessing interval must be positive.");
        }

        var captures = new List<AiReprocessCatalogRecord>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT telemetry.capture_id,
                   capture.installation_id,
                   MIN(telemetry.interval_started_utc_ticks) AS interval_started_utc_ticks,
                   MIN(telemetry.captured_utc_ticks) AS captured_utc_ticks,
                   telemetry.artifact_identity,
                   EXISTS (
                       SELECT 1
                       FROM ai_analysis_artifacts AS artifact
                       WHERE artifact.capture_id = telemetry.capture_id) AS has_ai_description
            FROM screenshot_interval_telemetry AS telemetry
            LEFT JOIN screenshot_captures AS capture ON capture.capture_id = telemetry.capture_id
            WHERE telemetry.captured_utc_ticks >= $from
              AND telemetry.captured_utc_ticks < $to
            GROUP BY telemetry.capture_id, capture.installation_id, telemetry.artifact_identity
            ORDER BY captured_utc_ticks, telemetry.capture_id, telemetry.artifact_identity;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.Ticks);
        using var reader = command.ExecuteReader();
        string? captureId = null;
        string? installationId = null;
        DateTimeOffset intervalStartedAt = default;
        DateTimeOffset capturedAt = default;
        var hasAiDescription = false;
        var artifactIdentities = new List<string>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowCaptureId = reader.GetString(0);
            if (captureId is not null && !string.Equals(captureId, rowCaptureId, StringComparison.Ordinal))
            {
                captures.Add(new AiReprocessCatalogRecord(
                    captureId,
                    installationId,
                    intervalStartedAt,
                    capturedAt,
                    artifactIdentities.ToArray(),
                    HasTelemetry: true,
                    hasAiDescription));
                artifactIdentities.Clear();
            }

            captureId = rowCaptureId;
            installationId = ReadNullableString(reader, 1);
            intervalStartedAt = new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero);
            capturedAt = new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero);
            artifactIdentities.Add(reader.GetString(4));
            hasAiDescription = reader.GetInt32(5) == 1;
        }

        if (captureId is not null)
        {
            captures.Add(new AiReprocessCatalogRecord(
                captureId,
                installationId,
                intervalStartedAt,
                capturedAt,
                artifactIdentities.ToArray(),
                HasTelemetry: true,
                hasAiDescription));
        }

        return captures;
    }

    /// <summary>Loads persisted telemetry identities for one original screenshot capture.</summary>
    internal AiReprocessCatalogRecord? LoadScreenshotCapture(string captureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT capture.installation_id,
                   MIN(telemetry.interval_started_utc_ticks), MIN(telemetry.captured_utc_ticks),
                   telemetry.artifact_identity,
                   EXISTS (
                       SELECT 1 FROM ai_analysis_artifacts AS artifact
                       WHERE artifact.capture_id = $captureId)
            FROM screenshot_interval_telemetry AS telemetry
            LEFT JOIN screenshot_captures AS capture ON capture.capture_id = telemetry.capture_id
            WHERE telemetry.capture_id = $captureId
            GROUP BY capture.installation_id, telemetry.artifact_identity
            ORDER BY telemetry.artifact_identity;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        using var reader = command.ExecuteReader();
        string? installationId = null;
        DateTimeOffset intervalStartedAt = default;
        DateTimeOffset capturedAt = default;
        var hasAiDescription = false;
        var identities = new List<string>();
        while (reader.Read())
        {
            installationId = ReadNullableString(reader, 0);
            intervalStartedAt = new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero);
            capturedAt = new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero);
            identities.Add(reader.GetString(3));
            hasAiDescription = reader.GetInt32(4) == 1;
        }

        return identities.Count == 0
            ? null
            : new AiReprocessCatalogRecord(
                captureId,
                installationId,
                intervalStartedAt,
                capturedAt,
                identities,
                HasTelemetry: true,
                hasAiDescription);
    }

    /// <summary>Loads telemetry presence and AI-description state for a bounded set of capture identifiers.</summary>
    internal IReadOnlyDictionary<string, AiReprocessCapturePersistenceState> LoadAiReprocessCaptureStates(
        IEnumerable<string> captureIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captureIds);
        var ids = captureIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var states = new Dictionary<string, AiReprocessCapturePersistenceState>(StringComparer.Ordinal);
        using var connection = OpenConnection();
        foreach (var batch in ids.Chunk(400))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var telemetry = connection.CreateCommand())
            {
                var parameters = AddTextParameters(telemetry, batch, "$capture");
                telemetry.CommandText = $"""
                    SELECT capture_id, MIN(interval_started_utc_ticks), MIN(captured_utc_ticks)
                    FROM screenshot_interval_telemetry
                    WHERE capture_id IN ({parameters})
                    GROUP BY capture_id;
                    """;
                using var reader = telemetry.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var captureId = reader.GetString(0);
                    states[captureId] = new AiReprocessCapturePersistenceState(
                        captureId,
                        new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
                        new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
                        HasAiDescription: false);
                }
            }

            using (var descriptions = connection.CreateCommand())
            {
                var parameters = AddTextParameters(descriptions, batch, "$described");
                descriptions.CommandText = $"""
                    SELECT DISTINCT capture_id
                    FROM ai_analysis_artifacts
                    WHERE capture_id IN ({parameters});
                    """;
                using var reader = descriptions.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var captureId = reader.GetString(0);
                    states.TryGetValue(captureId, out var current);
                    states[captureId] = new AiReprocessCapturePersistenceState(
                        captureId,
                        current?.IntervalStartedAt,
                        current?.CapturedAt,
                        HasAiDescription: true);
                }
            }
        }

        return states;
    }

    /// <summary>Checks whether a successful AI description is linked to the supplied capture.</summary>
    internal bool HasAiDescription(string captureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM ai_analysis_artifacts WHERE capture_id = $captureId);";
        command.Parameters.AddWithValue("$captureId", captureId);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    /// <summary>Creates one immutable reprocessing checkpoint plan and all of its work items atomically.</summary>
    internal void CreateAiReprocessJob(
        AiReprocessJobRecord job,
        IReadOnlyList<AiReprocessJobItemRecord> items)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(items);
        ValidateAiReprocessJob(job, items);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var insertJob = connection.CreateCommand())
        {
            insertJob.Transaction = transaction;
            insertJob.CommandText = """
                INSERT INTO ai_reprocess_jobs (
                    job_id, created_utc_ticks, updated_utc_ticks, range_start_utc_ticks, range_end_utc_ticks,
                    selected_local_date, capture_origin, configuration_fingerprint, state, active_slot, total_captures,
                    total_screenshots, pause_reason)
                VALUES (
                    $jobId, $created, $updated, $from, $to, $selectedDate, $origin, $fingerprint, $state, 1,
                    $totalCaptures, $totalScreenshots, $pauseReason);
                """;
            Add(insertJob, "$jobId", job.JobId.ToString("N"));
            Add(insertJob, "$created", job.CreatedAt.UtcDateTime.Ticks);
            Add(insertJob, "$updated", job.UpdatedAt.UtcDateTime.Ticks);
            Add(insertJob, "$from", job.FromUtc.UtcDateTime.Ticks);
            Add(insertJob, "$to", job.ToUtc.UtcDateTime.Ticks);
            Add(insertJob, "$selectedDate", job.SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(insertJob, "$origin", job.CaptureOrigin);
            Add(insertJob, "$fingerprint", job.ConfigurationFingerprint);
            Add(insertJob, "$state", job.State);
            Add(insertJob, "$totalCaptures", job.TotalCaptures);
            Add(insertJob, "$totalScreenshots", job.TotalScreenshots);
            Add(insertJob, "$pauseReason", job.PauseReason);
            insertJob.ExecuteNonQuery();
        }

        using var insertItem = connection.CreateCommand();
        insertItem.Transaction = transaction;
        insertItem.CommandText = """
            INSERT INTO ai_reprocess_job_items (
                job_id, capture_id, ordinal, captured_utc_ticks, capture_origin, artifact_identities_json,
                screenshot_count, state, attempt_count, last_code, updated_utc_ticks)
            VALUES (
                $jobId, $captureId, $ordinal, $captured, $origin, $identities,
                $screenshotCount, $state, $attemptCount, $lastCode, $updated);
            """;
        foreach (var item in items.OrderBy(item => item.Ordinal))
        {
            insertItem.Parameters.Clear();
            Add(insertItem, "$jobId", item.JobId.ToString("N"));
            Add(insertItem, "$captureId", item.CaptureId);
            Add(insertItem, "$ordinal", item.Ordinal);
            Add(insertItem, "$captured", item.CapturedAt.UtcDateTime.Ticks);
            Add(insertItem, "$origin", item.CaptureOrigin);
            Add(insertItem, "$identities", JsonSerializer.Serialize(item.ArtifactIdentities, _json));
            Add(insertItem, "$screenshotCount", item.ScreenshotCount);
            Add(insertItem, "$state", item.State);
            Add(insertItem, "$attemptCount", item.AttemptCount);
            Add(insertItem, "$lastCode", item.LastCode);
            Add(insertItem, "$updated", item.UpdatedAt.UtcDateTime.Ticks);
            insertItem.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Loads one durable reprocessing job checkpoint.</summary>
    internal AiReprocessJobRecord? LoadAiReprocessJob(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, created_utc_ticks, updated_utc_ticks, range_start_utc_ticks, range_end_utc_ticks,
                   selected_local_date, capture_origin, configuration_fingerprint, state, total_captures, total_screenshots, pause_reason
            FROM ai_reprocess_jobs
            WHERE job_id = $jobId;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("N"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAiReprocessJob(reader) : null;
    }

    /// <summary>Loads the single non-terminal reprocessing job, when present.</summary>
    internal AiReprocessJobRecord? LoadActiveAiReprocessJob()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, created_utc_ticks, updated_utc_ticks, range_start_utc_ticks, range_end_utc_ticks,
                   selected_local_date, capture_origin, configuration_fingerprint, state, total_captures, total_screenshots, pause_reason
            FROM ai_reprocess_jobs
            WHERE active_slot = 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAiReprocessJob(reader) : null;
    }

    /// <summary>Lists all persisted items for one reprocessing job in their frozen order.</summary>
    internal IReadOnlyList<AiReprocessJobItemRecord> ListAiReprocessJobItems(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }

        var items = new List<AiReprocessJobItemRecord>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, capture_id, ordinal, captured_utc_ticks, capture_origin, artifact_identities_json,
                   screenshot_count, state, attempt_count, last_code, updated_utc_ticks
            FROM ai_reprocess_job_items
            WHERE job_id = $jobId
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("N"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadAiReprocessJobItem(reader));
        }

        return items;
    }

    /// <summary>Loads the next pending work item for one job.</summary>
    internal AiReprocessJobItemRecord? LoadNextAiReprocessJobItem(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id, capture_id, ordinal, captured_utc_ticks, capture_origin, artifact_identities_json,
                   screenshot_count, state, attempt_count, last_code, updated_utc_ticks
            FROM ai_reprocess_job_items
            WHERE job_id = $jobId AND state = 'pending'
            ORDER BY ordinal
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString("N"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAiReprocessJobItem(reader) : null;
    }

    /// <summary>Transitions a durable reprocessing job and releases its single-flight slot on terminal states.</summary>
    internal void UpdateAiReprocessJobState(Guid jobId, string state, string? pauseReason, DateTimeOffset updatedAt)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }
        ValidateAiReprocessJobState(state);
        var activeSlot = IsTerminalAiReprocessJobState(state) ? null : (int?)1;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ai_reprocess_jobs
            SET state = $state, active_slot = $activeSlot, pause_reason = $pauseReason, updated_utc_ticks = $updated
            WHERE job_id = $jobId;
            """;
        Add(command, "$state", state);
        Add(command, "$activeSlot", activeSlot);
        Add(command, "$pauseReason", pauseReason);
        Add(command, "$updated", updatedAt.UtcDateTime.Ticks);
        Add(command, "$jobId", jobId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The AI reprocessing job does not exist.");
        }
    }

    /// <summary>Transitions one durable work-item checkpoint.</summary>
    internal void UpdateAiReprocessJobItemState(
        Guid jobId,
        string captureId,
        string state,
        int attemptCount,
        string? lastCode,
        DateTimeOffset updatedAt)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        ValidateAiReprocessItemState(state);
        if (attemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ai_reprocess_job_items
            SET state = $state, attempt_count = $attemptCount, last_code = $lastCode, updated_utc_ticks = $updated
            WHERE job_id = $jobId AND capture_id = $captureId;
            """;
        Add(command, "$state", state);
        Add(command, "$attemptCount", attemptCount);
        Add(command, "$lastCode", lastCode);
        Add(command, "$updated", updatedAt.UtcDateTime.Ticks);
        Add(command, "$jobId", jobId.ToString("N"));
        Add(command, "$captureId", captureId);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The AI reprocessing job item does not exist.");
        }

        using var touchJob = connection.CreateCommand();
        touchJob.Transaction = transaction;
        touchJob.CommandText = "UPDATE ai_reprocess_jobs SET updated_utc_ticks = $updated WHERE job_id = $jobId;";
        Add(touchJob, "$updated", updatedAt.UtcDateTime.Ticks);
        Add(touchJob, "$jobId", jobId.ToString("N"));
        if (touchJob.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("The AI reprocessing job does not exist.");
        }

        transaction.Commit();
    }

    /// <summary>Recovers a job interrupted while one item was running into a resumable paused checkpoint.</summary>
    internal void RecoverInterruptedAiReprocessJob(Guid jobId, DateTimeOffset updatedAt)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(jobId));
        }
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = """
                UPDATE ai_reprocess_job_items AS item
                SET state = CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM ai_analysis_artifacts AS artifact
                            WHERE artifact.capture_id = item.capture_id)
                        THEN 'succeeded'
                        ELSE 'pending'
                    END,
                    last_code = CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM ai_analysis_artifacts AS artifact
                            WHERE artifact.capture_id = item.capture_id)
                        THEN 'ai.analyzed.recovered'
                        ELSE 'runtime_restart'
                    END,
                    updated_utc_ticks = $updated
                WHERE job_id = $jobId AND state = 'running';
                """;
            items.Parameters.AddWithValue("$updated", updatedAt.UtcDateTime.Ticks);
            items.Parameters.AddWithValue("$jobId", jobId.ToString("N"));
            items.ExecuteNonQuery();
        }

        using (var job = connection.CreateCommand())
        {
            job.Transaction = transaction;
            job.CommandText = """
                UPDATE ai_reprocess_jobs
                SET state = 'paused_by_user', pause_reason = 'runtime_restart', updated_utc_ticks = $updated
                WHERE job_id = $jobId AND state IN ('running', 'pause_requested');
                """;
            job.Parameters.AddWithValue("$updated", updatedAt.UtcDateTime.Ticks);
            job.Parameters.AddWithValue("$jobId", jobId.ToString("N"));
            job.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Removes terminal reprocessing checkpoints whose screenshot day is outside retention.</summary>
    internal int DeleteTerminalAiReprocessJobsBefore(DateTimeOffset cutoffUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ai_reprocess_jobs
            WHERE range_end_utc_ticks <= $cutoff
              AND state IN ('completed', 'completed_with_errors', 'failed');
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.UtcDateTime.Ticks);
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes expired capture provenance only after every persisted screenshot, OCR, AI, and
    /// reprocessing reference has been removed.
    /// </summary>
    internal int DeleteOrphanedScreenshotCapturesBefore(DateTimeOffset cutoffUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM screenshot_captures
            WHERE captured_utc_ticks < $cutoff
              AND NOT EXISTS (
                  SELECT 1 FROM screenshot_interval_telemetry AS telemetry
                  WHERE telemetry.capture_id = screenshot_captures.capture_id)
              AND NOT EXISTS (
                  SELECT 1 FROM screenshot_text_snapshots AS snapshot
                  WHERE snapshot.capture_id = screenshot_captures.capture_id)
              AND NOT EXISTS (
                  SELECT 1 FROM ai_analysis_artifacts AS artifact
                  WHERE artifact.capture_id = screenshot_captures.capture_id)
              AND capture_id NOT IN (
                  SELECT capture_id FROM ai_reprocess_job_items);
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.UtcDateTime.Ticks);
        return command.ExecuteNonQuery();
    }

    /// <summary>Deletes one capture provenance row only when no persisted child still references it.</summary>
    internal int DeleteOrphanedScreenshotCapture(string captureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM screenshot_captures
            WHERE capture_id = $captureId
              AND NOT EXISTS (
                  SELECT 1 FROM screenshot_interval_telemetry AS telemetry
                  WHERE telemetry.capture_id = screenshot_captures.capture_id)
              AND NOT EXISTS (
                  SELECT 1 FROM screenshot_text_snapshots AS snapshot
                  WHERE snapshot.capture_id = screenshot_captures.capture_id)
              AND NOT EXISTS (
                  SELECT 1 FROM ai_analysis_artifacts AS artifact
                  WHERE artifact.capture_id = screenshot_captures.capture_id)
              AND capture_id NOT IN (
                  SELECT capture_id FROM ai_reprocess_job_items);
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        return command.ExecuteNonQuery();
    }

    /// <summary>Deletes interval telemetry for one screenshot artifact identity.</summary>
    internal int DeleteScreenshotIntervalTelemetry(string artifactIdentity)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity))
        {
            throw new ArgumentException("Screenshot artifact identity is required.", nameof(artifactIdentity));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT capture_id FROM screenshot_interval_telemetry WHERE artifact_identity = $identity;";
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        var captureId = command.ExecuteScalar() as string;
        command.CommandText = "DELETE FROM screenshot_interval_telemetry WHERE artifact_identity = $identity;";
        var removed = command.ExecuteNonQuery();
        if (captureId is not null)
        {
            // Sibling displays still own the shared snapshot. The last artifact's telemetry
            // removal also removes its hardware evidence, including analysis-only cleanup.
            command.CommandText = """
                DELETE FROM capture_hardware_snapshots WHERE capture_id = $captureId
                AND NOT EXISTS (SELECT 1 FROM screenshot_interval_telemetry WHERE capture_id = $captureId);
                """;
            command.Parameters.AddWithValue("$captureId", captureId);
            removed += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return removed;
    }

    /// <summary>Loads the persisted text snapshot for one screenshot artifact identity.</summary>
    internal ScreenshotTextSnapshot? LoadScreenshotTextSnapshot(string artifactIdentity)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity))
        {
            throw new ArgumentException("Screenshot artifact identity is required.", nameof(artifactIdentity));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_json FROM screenshot_text_snapshots WHERE artifact_identity = $identity;";
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        return command.ExecuteScalar() is not string payload
            ? null
            : JsonSerializer.Deserialize<ScreenshotTextSnapshot>(payload, _json)
                ?? throw new InvalidDataException("Persisted screenshot text snapshot is invalid.");
    }

    /// <summary>Loads OCR snapshots for a bounded set of screenshot artifact identities.</summary>
    internal IReadOnlyDictionary<string, ScreenshotTextSnapshot> LoadScreenshotTextSnapshots(
        IEnumerable<string> artifactIdentities,
        CancellationToken cancellationToken)
    {
        var identities = NormalizeArtifactIdentities(artifactIdentities);
        var snapshots = new Dictionary<string, ScreenshotTextSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (identities.Length == 0)
        {
            return snapshots;
        }

        using var connection = OpenConnection();
        foreach (var batch in identities.Chunk(400))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            var parameters = AddIdentityParameters(command, batch);
            command.CommandText = $"""
                SELECT artifact_identity, snapshot_json
                FROM screenshot_text_snapshots
                WHERE artifact_identity IN ({parameters});
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(
                    reader.GetString(0),
                    JsonSerializer.Deserialize<ScreenshotTextSnapshot>(reader.GetString(1), _json)
                        ?? throw new InvalidDataException("Persisted screenshot text snapshot is invalid."));
            }
        }

        return snapshots;
    }

    /// <summary>Loads every OCR snapshot belonging to one capture without scanning unrelated rows.</summary>
    internal IReadOnlyDictionary<string, ScreenshotTextSnapshot> LoadScreenshotTextSnapshotsForCapture(
        string captureId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT artifact_identity, snapshot_json
            FROM screenshot_text_snapshots
            WHERE capture_id = $captureId
            ORDER BY artifact_identity;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        using var reader = command.ExecuteReader();
        var snapshots = new Dictionary<string, ScreenshotTextSnapshot>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshots.Add(
                reader.GetString(0),
                JsonSerializer.Deserialize<ScreenshotTextSnapshot>(reader.GetString(1), _json)
                    ?? throw new InvalidDataException("Persisted screenshot text snapshot is invalid."));
        }

        return snapshots;
    }

    /// <summary>Visits every persisted screenshot text snapshot for deterministic search-index rebuilds.</summary>
    internal void VisitScreenshotTextSnapshots(
        Action<string, string, ScreenshotTextSnapshot> visitor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT artifact_identity, capture_id, snapshot_json
            FROM screenshot_text_snapshots
            ORDER BY artifact_identity;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = JsonSerializer.Deserialize<ScreenshotTextSnapshot>(reader.GetString(2), _json)
                ?? throw new InvalidDataException("Persisted screenshot text snapshot is invalid.");
            visitor(reader.GetString(0), reader.GetString(1), snapshot);
        }
    }

    /// <summary>Deletes the text snapshot associated with one screenshot artifact identity.</summary>
    internal int DeleteScreenshotTextSnapshot(string artifactIdentity)
    {
        if (string.IsNullOrWhiteSpace(artifactIdentity))
        {
            throw new ArgumentException("Screenshot artifact identity is required.", nameof(artifactIdentity));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM screenshot_text_snapshots WHERE artifact_identity = $identity;";
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        return command.ExecuteNonQuery();
    }

    /// <summary>Appends one standalone AI provider attempt without persisting provider error text or request content.</summary>
    internal void AppendStandaloneAiRequest(AiRequestUsageRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertAiRequest(connection, transaction, request);
        transaction.Commit();
    }

    /// <summary>Appends one successful AI attempt, result, and search index entry in one SQLite transaction.</summary>
    internal void AppendSuccessfulAiRequestAndAnalysis(AiRequestUsageRecord request, AiAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(analysis);
        if (!request.Success || string.IsNullOrWhiteSpace(analysis.CorrelationId)
            || !string.Equals(request.CorrelationId, analysis.CorrelationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Successful AI request and analysis must share a non-empty correlation identifier.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertAiRequest(connection, transaction, request);
        InsertAiAnalysisResult(connection, transaction, request, analysis);
        InsertAiAnalysisArtifacts(connection, transaction, analysis);
        transaction.Commit();
    }

    /// <summary>Replaces every cached AI pricing row for one provider inside a single transaction.</summary>
    internal void ReplaceAiModelPricing(string provider, IReadOnlyList<AiModelPricing> prices)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("AI pricing provider is required.", nameof(provider));
        }

        ArgumentNullException.ThrowIfNull(prices);
        if (prices.Count == 0)
        {
            throw new ArgumentException("AI pricing refresh must contain at least one row.", nameof(prices));
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        foreach (var price in prices)
        {
            ValidateAiModelPricing(normalizedProvider, price);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM ai_model_pricing WHERE provider = $provider;";
        delete.Parameters.AddWithValue("$provider", normalizedProvider);
        delete.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ai_model_pricing (
                provider, model, service_tier, context_window, currency, input_microusd_per_million,
                cached_input_microusd_per_million, cache_write_microusd_per_million,
                output_microusd_per_million, source_url, source_retrieved_utc_ticks)
            VALUES (
                $provider, $model, $serviceTier, $contextWindow, $currency, $input,
                $cachedInput, $cacheWrite, $output, $sourceUrl, $sourceRetrievedAt);
            """;
        var providerParameter = insert.Parameters.Add("$provider", SqliteType.Text);
        var modelParameter = insert.Parameters.Add("$model", SqliteType.Text);
        var serviceTierParameter = insert.Parameters.Add("$serviceTier", SqliteType.Text);
        var contextWindowParameter = insert.Parameters.Add("$contextWindow", SqliteType.Text);
        var currencyParameter = insert.Parameters.Add("$currency", SqliteType.Text);
        var inputParameter = insert.Parameters.Add("$input", SqliteType.Integer);
        var cachedInputParameter = insert.Parameters.Add("$cachedInput", SqliteType.Integer);
        var cacheWriteParameter = insert.Parameters.Add("$cacheWrite", SqliteType.Integer);
        var outputParameter = insert.Parameters.Add("$output", SqliteType.Integer);
        var sourceUrlParameter = insert.Parameters.Add("$sourceUrl", SqliteType.Text);
        var sourceRetrievedAtParameter = insert.Parameters.Add("$sourceRetrievedAt", SqliteType.Integer);

        foreach (var price in prices)
        {
            providerParameter.Value = normalizedProvider;
            modelParameter.Value = price.Model.Trim();
            serviceTierParameter.Value = price.ServiceTier.Trim().ToLowerInvariant();
            contextWindowParameter.Value = price.ContextWindow.Trim().ToLowerInvariant();
            currencyParameter.Value = price.Currency.Trim().ToLowerInvariant();
            inputParameter.Value = ToMicroUsd(price.InputUsdPerMillionTokens)!.Value;
            cachedInputParameter.Value = ToDbValue(ToMicroUsd(price.CachedInputUsdPerMillionTokens));
            cacheWriteParameter.Value = ToDbValue(ToMicroUsd(price.CacheWriteUsdPerMillionTokens));
            outputParameter.Value = ToMicroUsd(price.OutputUsdPerMillionTokens)!.Value;
            sourceUrlParameter.Value = price.SourceUrl.Trim();
            sourceRetrievedAtParameter.Value = price.SourceRetrievedAt.UtcDateTime.Ticks;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Lists the cached AI model pricing rows for one provider.</summary>
    internal IReadOnlyList<AiModelPricing> ListAiModelPricing(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("AI pricing provider is required.", nameof(provider));
        }

        var results = new List<AiModelPricing>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider, model, service_tier, context_window, currency, input_microusd_per_million,
                   cached_input_microusd_per_million, cache_write_microusd_per_million,
                   output_microusd_per_million, source_url, source_retrieved_utc_ticks
            FROM ai_model_pricing
            WHERE provider = $provider
            ORDER BY provider, model, service_tier, context_window;
            """;
        command.Parameters.AddWithValue("$provider", provider.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadAiModelPricing(reader));
        }

        return results;
    }

    /// <summary>Gets the newest retrieved-at timestamp for one cached AI pricing provider.</summary>
    internal DateTimeOffset? GetLatestAiModelPricingRetrievedAt(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("AI pricing provider is required.", nameof(provider));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(source_retrieved_utc_ticks) FROM ai_model_pricing WHERE provider = $provider;";
        command.Parameters.AddWithValue("$provider", provider.Trim().ToLowerInvariant());
        return ReadScalarNullableLong(command) is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    /// <summary>Counts persisted visual AI provider attempts in a half-open UTC interval.</summary>
    internal int CountAiVisualProviderRequests(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM ai_request_usage
            WHERE occurred_utc_ticks >= $from
              AND occurred_utc_ticks < $to
              AND request_kind IN ('screen_analysis', 'ocr_refinement', 'report_summary');
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.Ticks);
        return checked(Convert.ToInt32(command.ExecuteScalar()));
    }

    /// <summary>Loads the latest AI analysis from the current SQLite store.</summary>
    internal AiAnalysis? LoadLatestAiAnalysis()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT correlation_id, timestamp_utc_ticks, application, context, summary, installation_id,
                   screenshot_paths, origin, informational_schedule
            FROM ai_analysis_results
            ORDER BY timestamp_utc_ticks DESC, correlation_id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadAiAnalysis(reader);
    }

    /// <summary>Loads one successful AI analysis by its stable correlation identifier.</summary>
    internal AiAnalysis? LoadAiAnalysis(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT correlation_id, timestamp_utc_ticks, application, context, summary, installation_id,
                   screenshot_paths, origin, informational_schedule
            FROM ai_analysis_results
            WHERE correlation_id = $correlationId;
            """;
        command.Parameters.AddWithValue("$correlationId", correlationId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAiAnalysis(reader) : null;
    }

    /// <summary>Visits every persisted successful AI analysis for search-index rebuilds.</summary>
    internal void VisitAllAiAnalyses(Action<AiAnalysis> visitor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT correlation_id, timestamp_utc_ticks, application, context, summary, installation_id,
                   screenshot_paths, origin, informational_schedule
            FROM ai_analysis_results
            ORDER BY timestamp_utc_ticks, correlation_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(ReadAiAnalysis(reader));
        }
    }

    /// <summary>Loads the newest successful analysis that references each requested screenshot path.</summary>
    internal IReadOnlyDictionary<string, AiAnalysis> LoadLatestAiAnalysesForScreenshots(IEnumerable<string> screenshotPaths)
    {
        ArgumentNullException.ThrowIfNull(screenshotPaths);
        var requestedPaths = screenshotPaths
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var analyses = new Dictionary<string, AiAnalysis>(StringComparer.OrdinalIgnoreCase);
        if (requestedPaths.Count == 0)
        {
            return analyses;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT correlation_id, timestamp_utc_ticks, application, context, summary, installation_id,
                   screenshot_paths, origin, informational_schedule
            FROM ai_analysis_results
            WHERE screenshot_paths IS NOT NULL
            ORDER BY timestamp_utc_ticks DESC, correlation_id DESC;
            """;
        using var reader = command.ExecuteReader();
        while (analyses.Count < requestedPaths.Count && reader.Read())
        {
            var referencedPaths = EnumerateScreenshotPaths(ReadNullableString(reader, 6))
                .Select(Path.GetFullPath)
                .Where(requestedPaths.Contains)
                .Where(path => !analyses.ContainsKey(path))
                .ToArray();
            if (referencedPaths.Length == 0)
            {
                continue;
            }

            var analysis = ReadAiAnalysis(reader);
            foreach (var referencedPath in referencedPaths)
            {
                analyses.Add(referencedPath, analysis);
            }
        }

        return analyses;
    }

    /// <summary>Removes one screenshot artifact from its AI analysis, deleting the result only when no artifacts remain.</summary>
    internal int DeleteAiAnalysesReferencingScreenshot(string screenshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        var normalizedPath = Path.GetFullPath(screenshotPath);
        var deletionPlans = new List<(string CorrelationId, string ArtifactIdentity, string[] RemainingPaths)>();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT correlation_id, screenshot_paths FROM ai_analysis_results WHERE screenshot_paths IS NOT NULL;";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var correlationId = reader.GetString(0);
                var paths = EnumerateScreenshotPaths(reader.GetString(1));
                var matchingPaths = paths
                    .Where(path => string.Equals(Path.GetFullPath(path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matchingPaths.Length == 0)
                {
                    continue;
                }

                var artifactIdentities = paths.Select(ArtifactIdentityFromScreenshotPath).ToArray();
                if (matchingPaths.Length != 1
                    || artifactIdentities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
                {
                    throw new InvalidDataException("AI analysis screenshot paths must identify distinct retained artifacts.");
                }

                deletionPlans.Add((
                    correlationId,
                    ArtifactIdentityFromScreenshotPath(matchingPaths[0]),
                    paths.Where(path => !string.Equals(Path.GetFullPath(path), normalizedPath, StringComparison.OrdinalIgnoreCase)).ToArray()));
            }
        }

        if (deletionPlans.Count == 0)
        {
            transaction.Commit();
            return 0;
        }

        // The current schema materializes one relation for every path. Validate the complete relation
        // before changing anything so inconsistent modern records fail atomically instead of degrading silently.
        foreach (var plan in deletionPlans)
        {
            using var selectArtifacts = connection.CreateCommand();
            selectArtifacts.Transaction = transaction;
            selectArtifacts.CommandText = "SELECT artifact_identity, capture_id FROM ai_analysis_artifacts WHERE correlation_id = $correlationId;";
            selectArtifacts.Parameters.AddWithValue("$correlationId", plan.CorrelationId);
            var linkedArtifactIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = selectArtifacts.ExecuteReader();
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), plan.CorrelationId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("AI analysis artifact capture identity does not match its correlation identifier.");
                }

                linkedArtifactIdentities.Add(reader.GetString(0));
            }

            var expectedArtifactIdentities = plan.RemainingPaths
                .Select(ArtifactIdentityFromScreenshotPath)
                .Append(plan.ArtifactIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!linkedArtifactIdentities.SetEquals(expectedArtifactIdentities))
            {
                throw new InvalidDataException("AI analysis screenshot paths and artifact relations do not match.");
            }
        }

        using var deleteArtifact = connection.CreateCommand();
        deleteArtifact.Transaction = transaction;
        deleteArtifact.CommandText = "DELETE FROM ai_analysis_artifacts WHERE correlation_id = $correlationId AND artifact_identity = $artifactIdentity;";
        using var deleteSearch = connection.CreateCommand();
        deleteSearch.Transaction = transaction;
        deleteSearch.CommandText = "DELETE FROM ai_analysis_search WHERE correlation_id = $correlationId;";
        using var deleteResults = connection.CreateCommand();
        deleteResults.Transaction = transaction;
        deleteResults.CommandText = "DELETE FROM ai_analysis_results WHERE correlation_id = $correlationId;";
        using var updateResults = connection.CreateCommand();
        updateResults.Transaction = transaction;
        updateResults.CommandText = "UPDATE ai_analysis_results SET screenshot_paths = $paths WHERE correlation_id = $correlationId;";
        foreach (var plan in deletionPlans)
        {
            deleteArtifact.Parameters.Clear();
            deleteArtifact.Parameters.AddWithValue("$correlationId", plan.CorrelationId);
            deleteArtifact.Parameters.AddWithValue("$artifactIdentity", plan.ArtifactIdentity);
            if (deleteArtifact.ExecuteNonQuery() != 1)
            {
                throw new InvalidDataException("AI analysis artifact relation disappeared during deletion.");
            }

            if (plan.RemainingPaths.Length == 0)
            {
                deleteSearch.Parameters.Clear();
                deleteSearch.Parameters.AddWithValue("$correlationId", plan.CorrelationId);
                if (deleteSearch.ExecuteNonQuery() != 1)
                {
                    throw new InvalidDataException("AI analysis search document does not match its result.");
                }

                deleteResults.Parameters.Clear();
                deleteResults.Parameters.AddWithValue("$correlationId", plan.CorrelationId);
                if (deleteResults.ExecuteNonQuery() != 1)
                {
                    throw new InvalidDataException("AI analysis result disappeared during deletion.");
                }
            }
            else
            {
                updateResults.Parameters.Clear();
                updateResults.Parameters.AddWithValue("$paths", string.Join(';', plan.RemainingPaths));
                updateResults.Parameters.AddWithValue("$correlationId", plan.CorrelationId);
                if (updateResults.ExecuteNonQuery() != 1)
                {
                    throw new InvalidDataException("AI analysis result disappeared during artifact disassociation.");
                }
            }
        }

        transaction.Commit();
        return deletionPlans.Count;
    }

    /// <summary>Reads the semicolon-separated screenshot path list used by the current SQLite schema.</summary>
    internal static string[] EnumerateScreenshotPaths(string? screenshotPaths) =>
        screenshotPaths?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];

    private static AiReprocessJobRecord ReadAiReprocessJob(SqliteDataReader reader) => new(
        Guid.ParseExact(reader.GetString(0), "N"),
        new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
        new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
        new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
        new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
        DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None),
        ReadNullableString(reader, 6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetInt32(9),
        reader.GetInt32(10),
        ReadNullableString(reader, 11));

    private AiReprocessJobItemRecord ReadAiReprocessJobItem(SqliteDataReader reader)
    {
        var identities = JsonSerializer.Deserialize<string[]>(reader.GetString(5), _json)
            ?? throw new InvalidDataException("Persisted AI reprocessing artifact identities are invalid.");
        if (identities.Length == 0 || identities.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Persisted AI reprocessing artifact identities are empty.");
        }

        return new AiReprocessJobItemRecord(
            Guid.ParseExact(reader.GetString(0), "N"),
            reader.GetString(1),
            reader.GetInt32(2),
            new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
            reader.GetString(4),
            identities,
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetInt32(8),
            ReadNullableString(reader, 9),
            new DateTimeOffset(reader.GetInt64(10), TimeSpan.Zero));
    }

    private static void ValidateAiReprocessJob(
        AiReprocessJobRecord job,
        IReadOnlyList<AiReprocessJobItemRecord> items)
    {
        if (job.JobId == Guid.Empty)
        {
            throw new ArgumentException("AI reprocessing job identifier is required.", nameof(job));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ConfigurationFingerprint);
        ValidateAiReprocessJobState(job.State);
        if (IsTerminalAiReprocessJobState(job.State)
            || job.FromUtc >= job.ToUtc
            || items.Count == 0
            || job.TotalCaptures != items.Count
            || job.TotalScreenshots != items.Sum(item => item.ScreenshotCount)
            || job.TotalCaptures <= 0
            || job.TotalScreenshots <= 0)
        {
            throw new ArgumentException("The AI reprocessing job checkpoint is invalid.", nameof(job));
        }

        var expectedOrdinals = Enumerable.Range(0, items.Count).ToArray();
        if (!items.Select(item => item.Ordinal).Order().SequenceEqual(expectedOrdinals)
            || items.Any(item => item.JobId != job.JobId
                || !Guid.TryParseExact(item.CaptureId, "N", out _)
                || string.IsNullOrWhiteSpace(item.CaptureOrigin)
                || item.ArtifactIdentities.Count == 0
                || item.ArtifactIdentities.Any(string.IsNullOrWhiteSpace)
                || item.ArtifactIdentities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.ArtifactIdentities.Count
                || item.ArtifactIdentities.Any(identity =>
                    !string.Equals(Path.GetFileName(identity), identity, StringComparison.Ordinal)
                    || !ScreenCaptureService.IsOwnedArtifact(identity + ".webp")
                    || !identity.StartsWith(item.CaptureId + "_", StringComparison.Ordinal))
                || item.ScreenshotCount != item.ArtifactIdentities.Count
                || item.AttemptCount < 0))
        {
            throw new ArgumentException("The AI reprocessing work-item plan is invalid.", nameof(items));
        }

        foreach (var item in items)
        {
            ValidateAiReprocessItemState(item.State);
        }
    }

    private static void ValidateAiReprocessJobState(string state)
    {
        if (state is not ("pending" or "running" or "pause_requested" or "paused_by_user" or "paused_daily_quota"
            or "completed" or "completed_with_errors" or "failed"))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Unsupported AI reprocessing job state.");
        }
    }

    private static void ValidateAiReprocessItemState(string state)
    {
        if (state is not ("pending" or "running" or "succeeded" or "skipped" or "failed"))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Unsupported AI reprocessing item state.");
        }
    }

    private static bool IsTerminalAiReprocessJobState(string state) =>
        state is "completed" or "completed_with_errors" or "failed";

    /// <summary>Streams activity and AI usage from one SQLite read transaction and therefore one database snapshot.</summary>
    internal void VisitReportData(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<ReportSourceSample> activityVisitor,
        Action<AiRequestUsageRecord> aiUsageVisitor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityVisitor);
        ArgumentNullException.ThrowIfNull(aiUsageVisitor);
        ValidateInterval(fromUtc, toUtc, "report");

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        VisitReportOverlapping(connection, transaction, fromUtc, toUtc, activityVisitor, cancellationToken);
        VisitAiUsage(connection, transaction, fromUtc, toUtc, aiUsageVisitor, cancellationToken);
        transaction.Commit();
    }

    /// <summary>Streams only the activity fields required for aggregate reports.</summary>
    internal void VisitReportOverlapping(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<ReportSourceSample> visitor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ValidateInterval(fromUtc, toUtc, "activity");
        using var connection = OpenConnection();
        VisitReportOverlapping(connection, null, fromUtc, toUtc, visitor, cancellationToken);
    }

    private static void VisitReportOverlapping(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<ReportSourceSample> visitor,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT timestamp_utc_ticks, timestamp_offset_minutes, duration_seconds, state, application,
                   key_presses, mouse_clicks, installation_id
            FROM activity_samples
            WHERE timestamp_utc_ticks > $from AND start_utc_ticks < $to
            ORDER BY start_utc_ticks, timestamp_utc_ticks, id;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.Ticks);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timestamp = new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(reader.GetInt32(1)));
            visitor(new ReportSourceSample(
                timestamp,
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetString(7)));
        }
    }

    /// <summary>Streams sanitized AI request telemetry inside a half-open UTC interval.</summary>
    internal void VisitAiUsage(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<AiRequestUsageRecord> visitor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ValidateInterval(fromUtc, toUtc, "AI usage");
        using var connection = OpenConnection();
        VisitAiUsage(connection, null, fromUtc, toUtc, visitor, cancellationToken);
    }

    private static void VisitAiUsage(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Action<AiRequestUsageRecord> visitor,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT
                attempt_id, correlation_id, occurred_utc_ticks, completed_utc_ticks, origin, request_kind, provider,
                endpoint_host, requested_model, returned_model, provider_response_id, provider_request_id, http_status,
                elapsed_ms, provider_processing_ms, image_count, prompt_characters, max_output_tokens, input_tokens,
                output_tokens, total_tokens, cached_input_tokens, cache_write_tokens, cache_creation_input_tokens,
                cache_read_input_tokens, reasoning_tokens, thinking_tokens, reported_cost_microusd,
                reported_upstream_cost_microusd, finish_reason, success, failure_code
            FROM ai_request_usage
            WHERE occurred_utc_ticks >= $from AND occurred_utc_ticks < $to
            ORDER BY occurred_utc_ticks, attempt_id;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.Ticks);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(ReadAiRequestUsage(reader));
        }
    }

    private static string[] NormalizeArtifactIdentities(IEnumerable<string> artifactIdentities)
    {
        ArgumentNullException.ThrowIfNull(artifactIdentities);
        var identities = artifactIdentities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (identities.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Screenshot artifact identities cannot be empty.", nameof(artifactIdentities));
        }

        return identities;
    }

    private static string AddIdentityParameters(SqliteCommand command, IReadOnlyList<string> identities)
        => AddTextParameters(command, identities, "$identity");

    private static string AddTextParameters(
        SqliteCommand command,
        IReadOnlyList<string> values,
        string parameterPrefix)
    {
        var parameterNames = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var name = $"{parameterPrefix}{index}";
            parameterNames[index] = name;
            command.Parameters.AddWithValue(name, values[index]);
        }

        return string.Join(", ", parameterNames);
    }

    private static void ValidateInterval(DateTimeOffset fromUtc, DateTimeOffset toUtc, string description)
    {
        if (toUtc <= fromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(toUtc), $"The {description} query interval must be positive.");
        }
    }

    /// <summary>Streams every activity sample overlapping the supplied half-open UTC interval exactly once.</summary>
    internal void VisitOverlapping(DateTimeOffset fromUtc, DateTimeOffset toUtc, Action<ActivitySample> visitor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (toUtc <= fromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(toUtc), "The activity query interval must be positive.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                timestamp_utc_ticks, timestamp_offset_minutes, duration_seconds, state, process_name, application,
                context, window_title, installation_id, key_presses, mouse_clicks, attributes_json
            FROM activity_samples
            WHERE timestamp_utc_ticks > $from AND start_utc_ticks < $to
            ORDER BY start_utc_ticks, timestamp_utc_ticks, id;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.Ticks);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(ReadSample(reader));
        }
    }

    /// <summary>Visits every retained activity sample with its stable SQLite identifier for search indexing.</summary>
    internal void VisitAllActivitySamples(Action<long, ActivitySample> visitor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                timestamp_utc_ticks, timestamp_offset_minutes, duration_seconds, state, process_name, application,
                context, window_title, installation_id, key_presses, mouse_clicks, attributes_json, id
            FROM activity_samples
            ORDER BY timestamp_utc_ticks, id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(reader.GetInt64(12), ReadSample(reader));
        }
    }

    /// <summary>Loads the most recently completed activity sample.</summary>
    internal ActivitySample? LoadLatest()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                timestamp_utc_ticks, timestamp_offset_minutes, duration_seconds, state, process_name, application,
                context, window_title, installation_id, key_presses, mouse_clicks, attributes_json
            FROM activity_samples
            ORDER BY timestamp_utc_ticks DESC, id DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSample(reader) : null;
    }

    /// <summary>Counts expired activity and AI rows without changing them.</summary>
    internal (int Count, long Bytes) GetRetentionPreview(DateTimeOffset cutoffUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM activity_samples WHERE timestamp_utc_ticks < $cutoff),
                (SELECT COALESCE(SUM(estimated_bytes), 0) FROM activity_samples WHERE timestamp_utc_ticks < $cutoff),
                (SELECT COUNT(*)
                 FROM ai_request_usage AS request
                 WHERE request.occurred_utc_ticks < $cutoff
                   AND NOT EXISTS (
                       SELECT 1
                       FROM ai_analysis_results AS result
                       WHERE result.attempt_id = request.attempt_id
                         AND result.timestamp_utc_ticks >= $cutoff)),
                (SELECT COUNT(*) FROM ai_analysis_results WHERE timestamp_utc_ticks < $cutoff),
                (SELECT COALESCE(SUM(
                    length(provider) + length(endpoint_host) + length(requested_model) + length(origin) + length(request_kind)), 0)
                 FROM ai_request_usage AS request
                 WHERE request.occurred_utc_ticks < $cutoff
                   AND NOT EXISTS (
                       SELECT 1
                       FROM ai_analysis_results AS result
                       WHERE result.attempt_id = request.attempt_id
                         AND result.timestamp_utc_ticks >= $cutoff)),
                (SELECT COALESCE(SUM(length(application) + length(context) + length(summary)), 0)
                 FROM ai_analysis_results WHERE timestamp_utc_ticks < $cutoff),
                (SELECT COUNT(*) FROM screenshot_text_snapshots WHERE extracted_utc_ticks < $cutoff),
                (SELECT COALESCE(SUM(length(snapshot_json) + length(source_path)), 0) FROM screenshot_text_snapshots WHERE extracted_utc_ticks < $cutoff),
                (SELECT COUNT(*) FROM screenshot_interval_telemetry WHERE captured_utc_ticks < $cutoff),
                (SELECT COUNT(*) FROM capture_hardware_snapshots WHERE capture_id IN
                    (SELECT capture_id FROM screenshot_captures WHERE captured_utc_ticks < $cutoff)),
                (SELECT COALESCE(SUM(length(snapshot_json)), 0) FROM capture_hardware_snapshots WHERE capture_id IN
                    (SELECT capture_id FROM screenshot_captures WHERE captured_utc_ticks < $cutoff));
            """;
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.UtcDateTime.Ticks);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("SQLite did not return a retention aggregate.");
        }

        return (
            checked(reader.GetInt32(0) + reader.GetInt32(2) + reader.GetInt32(3) + reader.GetInt32(6) + reader.GetInt32(8) + reader.GetInt32(9)),
            checked(reader.GetInt64(1) + reader.GetInt64(4) + reader.GetInt64(5) + reader.GetInt64(7) + reader.GetInt64(10)));
    }

    /// <summary>Deletes expired activity and AI rows, including their derived full-text search documents.</summary>
    internal int ApplyRetention(DateTimeOffset cutoffUtc)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var cutoff = cutoffUtc.UtcDateTime.Ticks;
        var removedAiResults = ExecuteDelete(connection, transaction, """
            DELETE FROM ai_analysis_search
            WHERE correlation_id IN (
                SELECT correlation_id FROM ai_analysis_results WHERE timestamp_utc_ticks < $cutoff);
            """, cutoff);
        _ = removedAiResults; // FTS documents are derived data and are not counted as separate retained records.
        var removedResults = ExecuteDelete(connection, transaction, """
            DELETE FROM ai_analysis_results WHERE timestamp_utc_ticks < $cutoff;
            """, cutoff);
        var removedRequests = ExecuteDelete(connection, transaction, """
            DELETE FROM ai_request_usage
            WHERE occurred_utc_ticks < $cutoff
              AND NOT EXISTS (
                  SELECT 1 FROM ai_analysis_results
                  WHERE ai_analysis_results.attempt_id = ai_request_usage.attempt_id);
            """, cutoff);
        var removedActivity = ExecuteDelete(connection, transaction, "DELETE FROM activity_samples WHERE timestamp_utc_ticks < $cutoff;", cutoff);
        var removedText = ExecuteDelete(connection, transaction, "DELETE FROM screenshot_text_snapshots WHERE extracted_utc_ticks < $cutoff;", cutoff);
        var removedTelemetry = ExecuteDelete(connection, transaction, "DELETE FROM screenshot_interval_telemetry WHERE captured_utc_ticks < $cutoff;", cutoff);
        var removedHardware = ExecuteDelete(connection, transaction, """
            DELETE FROM capture_hardware_snapshots WHERE capture_id IN
                (SELECT capture_id FROM screenshot_captures WHERE captured_utc_ticks < $cutoff);
            """, cutoff);
        transaction.Commit();
        return checked(removedActivity + removedRequests + removedResults + removedText + removedTelemetry + removedHardware);
    }

    private void InitializeSchema(bool databaseExisted)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_databasePath.ToUpperInvariant())))[..32];
        using var schemaMutex = new Mutex(false, $"Local\\WorkTrail.ActivityDb.{fingerprint}");
        var acquired = false;
        try
        {
            try
            {
                acquired = schemaMutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out while initializing the activity database schema.");
            }

            using var connection = OpenConnection();
            var version = ReadSchemaVersion(connection);
            if (version == 0)
            {
                if (databaseExisted || ReadApplicationSchemaObjects(connection).Count > 0)
                {
                    throw new InvalidOperationException(
                        "An unversioned activity database is not supported; remove it before starting WorkTrail.");
                }

                CreateSchema(connection);
                version = ReadSchemaVersion(connection);
            }

            if (version != SchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported activity database schema version {version}; expected {SchemaVersion}.");
            }

            ValidateSchema(connection);
            using var journal = connection.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode = WAL;";
            journal.ExecuteScalar();
        }
        finally
        {
            if (acquired)
            {
                schemaMutex.ReleaseMutex();
            }
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Validates a portable archive database against the only supported current schema.</summary>
    internal static void ValidateArchiveDatabaseSchema(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();

        var version = ReadSchemaVersion(connection);
        if (version != SchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported archive database schema version {version}; expected {SchemaVersion}.");
        }

        ValidateSchema(connection);
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ActivitySchemaSql + AiSchemaSql + ScreenshotTextSchemaSql + AiPricingSchemaSql
            + ScreenshotIntervalTelemetrySchemaSql + AiReprocessingSchemaSql + InstallationArchiveSchemaSql
            + SearchRevisionSchemaSql
            + CaptureHardwareSnapshotSchemaSql + CalendarSchemaSql
            + $"PRAGMA user_version = {SchemaVersion};";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Loads one retained activity sample by its stable SQLite identifier.</summary>
    internal ActivitySample? LoadActivitySample(long id)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                timestamp_utc_ticks, timestamp_offset_minutes, duration_seconds, state, process_name, application,
                context, window_title, installation_id, key_presses, mouse_clicks, attributes_json
            FROM activity_samples
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSample(reader) : null;
    }

    private static void ValidateSchema(SqliteConnection connection)
    {
        ValidatePriorSchema(connection);
        ValidateCalendarSchema(connection);
        if (!ReadApplicationSchemaObjects(connection).SetEquals(ExpectedApplicationSchemaObjects))
        {
            throw new InvalidOperationException("The activity database contains unsupported schema objects.");
        }
    }

    private static void ValidatePriorSchema(SqliteConnection connection)
    {
        ValidateBaseSchema(connection, ActivitySchemaSql, ExpectedActivityColumns, ExpectedActivityIndexes);
        ValidateScreenshotTextSchema(connection);
        ValidateScreenshotIntervalTelemetrySchema(connection);
        ValidateAiReprocessingSchema(connection);
        ValidateInstallationArchiveSchema(connection);
        ValidateSearchRevisionSchema(connection);
        ValidateCreateStatement(connection, "ai_model_pricing", AiPricingSchemaSql);
        if (!ReadColumns(connection, "ai_model_pricing").SequenceEqual(ExpectedAiModelPricingColumns)
            || !ReadIndexes(connection, "ai_model_pricing").SetEquals(ExpectedAiModelPricingIndexes))
        {
            throw new InvalidOperationException("The AI pricing schema does not match the supported schema.");
        }

        ValidateCreateStatement(connection, "capture_hardware_snapshots", CaptureHardwareSnapshotSchemaSql);
        if (!ReadColumns(connection, "capture_hardware_snapshots").SequenceEqual(ExpectedCaptureHardwareSnapshotColumns)
            || !ReadIndexes(connection, "capture_hardware_snapshots").SetEquals([
                "sqlite_autoindex_capture_hardware_snapshots_1", "ix_capture_hardware_snapshots_sampled"]))
        {
            throw new InvalidOperationException("The capture hardware snapshot schema does not match the supported schema.");
        }
    }

    private static void ValidateSearchRevisionSchema(SqliteConnection connection)
    {
        ValidateCreateStatement(connection, "search_change_log", SearchRevisionSchemaSql);
        if (!ReadColumns(connection, "search_change_log").SequenceEqual(ExpectedSearchChangeColumns)
            || ReadIndexes(connection, "search_change_log").Count != 0)
        {
            throw new InvalidOperationException("The search source revision schema does not match the supported schema.");
        }
    }

    private static void ValidateInstallationArchiveSchema(SqliteConnection connection)
    {
        ValidateCreateStatement(connection, "installation_profiles", InstallationArchiveSchemaSql);
        ValidateCreateStatement(connection, "screenshot_captures", InstallationArchiveSchemaSql);
        ValidateCreateStatement(connection, "archive_imports", InstallationArchiveSchemaSql);
        ValidateCreateStatement(connection, "store_metadata", InstallationArchiveSchemaSql);
        if (!ReadColumns(connection, "installation_profiles").SequenceEqual(ExpectedInstallationProfileColumns)
            || !ReadColumns(connection, "screenshot_captures").SequenceEqual(ExpectedScreenshotCaptureColumns)
            || !ReadColumns(connection, "archive_imports").SequenceEqual(ExpectedArchiveImportColumns)
            || !ReadColumns(connection, "store_metadata").SequenceEqual(ExpectedStoreMetadataColumns)
            || !ReadIndexes(connection, "installation_profiles").SetEquals(["sqlite_autoindex_installation_profiles_1"])
            || !ReadIndexes(connection, "screenshot_captures").SetEquals([
                "sqlite_autoindex_screenshot_captures_1",
                "ix_screenshot_captures_installation",
                "ix_screenshot_captures_captured"])
            || !ReadIndexes(connection, "archive_imports").SetEquals(["sqlite_autoindex_archive_imports_1"])
            || !ReadIndexes(connection, "store_metadata").SetEquals(["sqlite_autoindex_store_metadata_1"]))
        {
            throw new InvalidOperationException("The installation and archive schema does not match the supported schema.");
        }
    }

    private static void ValidateScreenshotTextSchema(SqliteConnection connection)
    {
        ValidateCreateStatement(connection, "screenshot_text_snapshots", ScreenshotTextSchemaSql);
        var actualScreenshotTextColumns = ReadColumns(connection, "screenshot_text_snapshots");
        if (!actualScreenshotTextColumns.SequenceEqual(ExpectedScreenshotTextSnapshotColumns))
        {
            throw new InvalidOperationException("The screenshot text snapshot schema does not match the supported schema.");
        }

        var screenshotTextIndexes = ReadIndexes(connection, "screenshot_text_snapshots");
        if (!screenshotTextIndexes.SetEquals(ExpectedScreenshotTextSnapshotIndexes))
        {
            throw new InvalidOperationException("The screenshot text snapshot indexes do not match the supported schema.");
        }
    }

    private static void ValidateScreenshotIntervalTelemetrySchema(SqliteConnection connection)
    {
        ValidateCreateStatement(connection, "screenshot_interval_telemetry", ScreenshotIntervalTelemetrySchemaSql);
        var actualColumns = ReadColumns(connection, "screenshot_interval_telemetry");
        if (!actualColumns.SequenceEqual(ExpectedScreenshotIntervalTelemetryColumns))
        {
            throw new InvalidOperationException("The screenshot interval telemetry schema does not match the supported schema.");
        }

        var indexes = ReadIndexes(connection, "screenshot_interval_telemetry");
        if (!indexes.SetEquals(ExpectedScreenshotIntervalTelemetryIndexes))
        {
            throw new InvalidOperationException("The screenshot interval telemetry indexes do not match the supported schema.");
        }
    }

    private static void ValidateBaseSchema(
        SqliteConnection connection,
        string activitySchemaSql,
        IReadOnlyList<SchemaColumn> expectedActivityColumns,
        IReadOnlySet<string> expectedActivityIndexes)
    {
        ValidateCreateStatement(connection, "activity_samples", activitySchemaSql);
        ValidateCreateStatement(connection, "ai_request_usage", AiSchemaSql);
        ValidateCreateStatement(connection, "ai_analysis_results", AiSchemaSql);
        ValidateCreateStatement(connection, "ai_analysis_search", AiSchemaSql);

        var actualActivityColumns = ReadColumns(connection, "activity_samples");
        if (!actualActivityColumns.SequenceEqual(expectedActivityColumns))
        {
            throw new InvalidOperationException("The activity database schema does not match the supported greenfield schema.");
        }

        ValidateColumnNames(connection, "ai_request_usage", ExpectedAiRequestUsageColumns);
        ValidateColumnNames(connection, "ai_analysis_results", ExpectedAiAnalysisResultColumns);

        var activityIndexes = ReadIndexes(connection, "activity_samples");
        if (!activityIndexes.SetEquals(expectedActivityIndexes))
        {
            throw new InvalidOperationException("The activity database indexes do not match the supported greenfield schema.");
        }

        var requestIndexes = ReadIndexes(connection, "ai_request_usage");
        if (!requestIndexes.SetEquals(ExpectedAiRequestUsageIndexes))
        {
            throw new InvalidOperationException("The AI usage indexes do not match the supported schema.");
        }

        var resultIndexes = ReadIndexes(connection, "ai_analysis_results");
        if (!resultIndexes.SetEquals(ExpectedAiAnalysisResultIndexes))
        {
            throw new InvalidOperationException("The AI result indexes do not match the supported schema.");
        }
    }

    private static void ValidateCreateStatement(SqliteConnection connection, string objectName, string schemaSql)
    {
        var marker = objectName == "ai_analysis_search"
            ? $"CREATE VIRTUAL TABLE {objectName}"
            : $"CREATE TABLE {objectName}";
        var start = schemaSql.IndexOf(marker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : schemaSql.IndexOf(';', start);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException($"The expected SQL for {objectName} is invalid.");
        }

        var expected = NormalizeSchemaSql(schemaSql[start..end]);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE name = $name;";
        command.Parameters.AddWithValue("$name", objectName);
        if (command.ExecuteScalar() is not string actual || !string.Equals(NormalizeSchemaSql(actual), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {objectName} schema does not match the supported greenfield schema.");
        }
    }

    private static string NormalizeSchemaSql(string sql) => string.Concat(
        sql.Where(character => !char.IsWhiteSpace(character) && character != ';'));

    private static HashSet<string> ReadApplicationSchemaObjects(SqliteConnection connection)
    {
        var objects = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            objects.Add(reader.GetString(0));
        }

        return objects;
    }

    private static List<SchemaColumn> ReadColumns(SqliteConnection connection, string table)
    {
        var actualColumns = new List<SchemaColumn>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            actualColumns.Add(new SchemaColumn(
                reader.GetString(1),
                reader.GetString(2).ToUpperInvariant(),
                reader.GetInt32(3) == 1,
                reader.GetInt32(5)));
        }

        return actualColumns;
    }

    private static void ValidateColumnNames(SqliteConnection connection, string table, IReadOnlyList<string> expected)
    {
        var actual = ReadColumns(connection, table).Select(column => column.Name).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The {table} table does not match the supported AI telemetry schema.");
        }
    }

    private static HashSet<string> ReadIndexes(SqliteConnection connection, string table)
    {
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(1));
        }

        return indexes;
    }

    private static void InsertAiRequest(SqliteConnection connection, SqliteTransaction transaction, AiRequestUsageRecord request)
    {
        ValidateAiRequest(request);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ai_request_usage (
                attempt_id, correlation_id, occurred_utc_ticks, completed_utc_ticks, origin, request_kind, provider,
                endpoint_host, requested_model, returned_model, provider_response_id, provider_request_id, http_status,
                elapsed_ms, provider_processing_ms, image_count, prompt_characters, max_output_tokens, input_tokens,
                output_tokens, total_tokens, cached_input_tokens, cache_write_tokens, cache_creation_input_tokens,
                cache_read_input_tokens, reasoning_tokens, thinking_tokens, reported_cost_microusd,
                reported_upstream_cost_microusd, cost_source, finish_reason, success, failure_code)
            VALUES (
                $attemptId, $correlationId, $occurred, $completed, $origin, $requestKind, $provider,
                $endpointHost, $requestedModel, $returnedModel, $providerResponseId, $providerRequestId, $httpStatus,
                $elapsed, $providerProcessing, $imageCount, $promptCharacters, $maxOutputTokens, $inputTokens,
                $outputTokens, $totalTokens, $cachedInputTokens, $cacheWriteTokens, $cacheCreationInputTokens,
                $cacheReadInputTokens, $reasoningTokens, $thinkingTokens, $reportedCost, $reportedUpstreamCost,
                $costSource, $finishReason, $success, $failureCode);
            """;
        Add(command, "$attemptId", request.AttemptId);
        Add(command, "$correlationId", request.CorrelationId);
        Add(command, "$occurred", request.OccurredAt.UtcDateTime.Ticks);
        Add(command, "$completed", request.CompletedAt?.UtcDateTime.Ticks);
        Add(command, "$origin", request.Origin);
        Add(command, "$requestKind", request.RequestKind);
        Add(command, "$provider", request.Provider);
        Add(command, "$endpointHost", request.EndpointHost);
        Add(command, "$requestedModel", request.RequestedModel);
        Add(command, "$returnedModel", request.ReturnedModel);
        Add(command, "$providerResponseId", request.ProviderResponseId);
        Add(command, "$providerRequestId", request.ProviderRequestId);
        Add(command, "$httpStatus", request.HttpStatusCode);
        Add(command, "$elapsed", request.ElapsedMilliseconds);
        Add(command, "$providerProcessing", request.ProviderProcessingMilliseconds);
        Add(command, "$imageCount", request.ImageCount);
        Add(command, "$promptCharacters", request.PromptCharacters);
        Add(command, "$maxOutputTokens", request.MaxOutputTokens);
        Add(command, "$inputTokens", request.Usage.InputTokens);
        Add(command, "$outputTokens", request.Usage.OutputTokens);
        Add(command, "$totalTokens", request.Usage.TotalTokens);
        Add(command, "$cachedInputTokens", request.Usage.CachedInputTokens);
        Add(command, "$cacheWriteTokens", request.Usage.CacheWriteTokens);
        Add(command, "$cacheCreationInputTokens", request.Usage.CacheCreationInputTokens);
        Add(command, "$cacheReadInputTokens", request.Usage.CacheReadInputTokens);
        Add(command, "$reasoningTokens", request.Usage.ReasoningTokens);
        Add(command, "$thinkingTokens", request.Usage.ThinkingTokens);
        Add(command, "$reportedCost", ToMicroUsd(request.Usage.ReportedCostUsd));
        Add(command, "$reportedUpstreamCost", ToMicroUsd(request.Usage.ReportedUpstreamCostUsd));
        Add(command, "$costSource", request.Usage.ReportedCostUsd.HasValue ? "provider" : "unavailable");
        Add(command, "$finishReason", request.FinishReason);
        Add(command, "$success", request.Success ? 1 : 0);
        Add(command, "$failureCode", request.FailureCode);
        command.ExecuteNonQuery();
    }

    private static void InsertAiAnalysisResult(SqliteConnection connection, SqliteTransaction transaction, AiRequestUsageRecord request, AiAnalysis analysis)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ai_analysis_results (
                correlation_id, attempt_id, timestamp_utc_ticks, snapshot_utc_ticks, application, context, summary,
                installation_id, origin, informational_schedule, screenshot_paths, image_count)
            VALUES (
                $correlationId, $attemptId, $timestamp, $snapshotTimestamp, $application, $context, $summary,
                $installationId, $origin, $schedule, $screenshotPaths, $imageCount);

            INSERT INTO ai_analysis_search (correlation_id, application, context, summary)
            VALUES ($correlationId, $application, $context, $summary);
            """;
        Add(command, "$correlationId", analysis.CorrelationId);
        Add(command, "$attemptId", request.AttemptId);
        Add(command, "$timestamp", analysis.Timestamp.UtcDateTime.Ticks);
        Add(command, "$snapshotTimestamp", analysis.Snapshot?.Timestamp.UtcDateTime.Ticks);
        Add(command, "$application", analysis.Application);
        Add(command, "$context", analysis.Context);
        Add(command, "$summary", analysis.Summary);
        Add(command, "$installationId", analysis.InstallationId);
        Add(command, "$origin", analysis.Origin ?? request.Origin);
        Add(command, "$schedule", analysis.InformationalSchedule);
        Add(command, "$screenshotPaths", analysis.ScreenshotPaths);
        Add(command, "$imageCount", request.ImageCount);
        command.ExecuteNonQuery();
    }

    private static void InsertAiAnalysisArtifacts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AiAnalysis analysis)
    {
        var paths = EnumerateScreenshotPaths(analysis.ScreenshotPaths);
        if (paths.Length == 0)
        {
            return;
        }

        if (!Guid.TryParseExact(analysis.CorrelationId, "N", out _))
        {
            throw new InvalidDataException("Screenshot analysis artifacts require a GUID N capture correlation identifier.");
        }

        var captureId = analysis.CorrelationId;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ai_analysis_artifacts (artifact_identity, capture_id, correlation_id)
            VALUES ($artifactIdentity, $captureId, $correlationId);
            """;
        var artifactParameter = command.Parameters.Add("$artifactIdentity", SqliteType.Text);
        var captureParameter = command.Parameters.Add("$captureId", SqliteType.Text);
        var correlationParameter = command.Parameters.Add("$correlationId", SqliteType.Text);
        captureParameter.Value = captureId;
        correlationParameter.Value = captureId;
        foreach (var path in paths)
        {
            var artifactIdentity = ArtifactIdentityFromScreenshotPath(path);
            if (!artifactIdentity.StartsWith(captureId + "_", StringComparison.Ordinal))
            {
                throw new InvalidDataException("AI analysis screenshot artifact identity does not match its capture correlation identifier.");
            }

            artifactParameter.Value = artifactIdentity;
            command.ExecuteNonQuery();
        }
    }

    private static void ValidateAiReprocessingSchema(SqliteConnection connection)
    {
        ValidateCreateStatement(connection, "ai_analysis_artifacts", AiReprocessingSchemaSql);
        ValidateCreateStatement(connection, "ai_reprocess_jobs", AiReprocessingSchemaSql);
        ValidateCreateStatement(connection, "ai_reprocess_job_items", AiReprocessingSchemaSql);

        if (!ReadColumns(connection, "ai_analysis_artifacts").SequenceEqual(ExpectedAiAnalysisArtifactColumns)
            || !ReadIndexes(connection, "ai_analysis_artifacts").SetEquals(ExpectedAiAnalysisArtifactIndexes))
        {
            throw new InvalidOperationException("The AI analysis artifact relation schema does not match the supported schema.");
        }

        if (!ReadColumns(connection, "ai_reprocess_jobs").SequenceEqual(ExpectedAiReprocessJobColumns)
            || !ReadIndexes(connection, "ai_reprocess_jobs").SetEquals(ExpectedAiReprocessJobIndexes))
        {
            throw new InvalidOperationException("The AI reprocessing job schema does not match the supported schema.");
        }

        if (!ReadColumns(connection, "ai_reprocess_job_items").SequenceEqual(ExpectedAiReprocessJobItemColumns)
            || !ReadIndexes(connection, "ai_reprocess_job_items").SetEquals(ExpectedAiReprocessJobItemIndexes))
        {
            throw new InvalidOperationException("The AI reprocessing item schema does not match the supported schema.");
        }
    }

    private static string ArtifactIdentityFromScreenshotPath(string screenshotPath)
    {
        if (!Path.IsPathFullyQualified(screenshotPath) || !ScreenCaptureService.IsOwnedArtifact(screenshotPath))
        {
            throw new InvalidDataException("AI analysis references an invalid WorkTrail screenshot artifact.");
        }

        var identity = Path.GetFileNameWithoutExtension(screenshotPath);
        if (identity.EndsWith("-raw", StringComparison.OrdinalIgnoreCase))
        {
            identity = identity[..^4];
        }

        return string.IsNullOrWhiteSpace(identity)
            ? throw new InvalidDataException("AI analysis references an invalid screenshot artifact identity.")
            : identity;
    }

    private static void ValidateAiRequest(AiRequestUsageRecord request)
    {
        if (string.IsNullOrWhiteSpace(request.AttemptId) || string.IsNullOrWhiteSpace(request.CorrelationId)
            || string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.RequestKind)
            || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.EndpointHost)
            || string.IsNullOrWhiteSpace(request.RequestedModel) || request.ImageCount < 0
            || request.PromptCharacters < 0 || request.MaxOutputTokens < 0)
        {
            throw new ArgumentException("AI usage record contains invalid required metadata.", nameof(request));
        }
    }

    private static void ValidateAiModelPricing(string provider, AiModelPricing price)
    {
        if (!string.Equals(price.Provider, provider, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(price.Model)
            || string.IsNullOrWhiteSpace(price.ServiceTier)
            || string.IsNullOrWhiteSpace(price.ContextWindow)
            || !string.Equals(price.Currency, "usd", StringComparison.OrdinalIgnoreCase)
            || price.InputUsdPerMillionTokens < 0m
            || price.CachedInputUsdPerMillionTokens is < 0m
            || price.CacheWriteUsdPerMillionTokens is < 0m
            || price.OutputUsdPerMillionTokens < 0m
            || string.IsNullOrWhiteSpace(price.SourceUrl))
        {
            throw new ArgumentException("AI pricing row contains invalid required metadata.", nameof(price));
        }
    }

    private static AiRequestUsageRecord ReadAiRequestUsage(SqliteDataReader reader)
    {
        var usage = new AiUsageMetrics(
            ReadNullableLong(reader, 18), ReadNullableLong(reader, 19), ReadNullableLong(reader, 20),
            ReadNullableLong(reader, 21), ReadNullableLong(reader, 22), ReadNullableLong(reader, 23),
            ReadNullableLong(reader, 24), ReadNullableLong(reader, 25), ReadNullableLong(reader, 26),
            FromMicroUsd(ReadNullableLong(reader, 27)), FromMicroUsd(ReadNullableLong(reader, 28)));
        return new AiRequestUsageRecord(
            reader.GetString(0), reader.GetString(1), new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero),
            ReadNullableLong(reader, 3) is { } completed ? new DateTimeOffset(completed, TimeSpan.Zero) : null,
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            ReadNullableString(reader, 9), ReadNullableString(reader, 10), ReadNullableString(reader, 11),
            ReadNullableInt(reader, 12), ReadNullableLong(reader, 13), ReadNullableLong(reader, 14), reader.GetInt32(15),
            reader.GetInt32(16), reader.GetInt32(17), usage, ReadNullableString(reader, 29), reader.GetInt32(30) == 1,
            ReadNullableString(reader, 31));
    }

    private static AiModelPricing ReadAiModelPricing(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        FromMicroUsd(reader.GetInt64(5))!.Value,
        FromMicroUsd(ReadNullableLong(reader, 6)),
        FromMicroUsd(ReadNullableLong(reader, 7)),
        FromMicroUsd(reader.GetInt64(8))!.Value,
        reader.GetString(9),
        new DateTimeOffset(reader.GetInt64(10), TimeSpan.Zero));

    private ActivitySample ReadSample(SqliteDataReader reader)
    {
        var timestampUtc = new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero);
        var timestamp = timestampUtc.ToOffset(TimeSpan.FromMinutes(reader.GetInt32(1)));
        IReadOnlyDictionary<string, string>? attributes = null;
        if (!reader.IsDBNull(11))
        {
            attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(11), _json)
                ?? throw new InvalidOperationException("Stored activity attributes are invalid.");
        }

        return new ActivitySample(
            timestamp, reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.GetString(8), reader.GetInt64(9), reader.GetInt64(10), attributes);
    }

    private SqliteConnection OpenConnection() => _connections.Open();

    private static int ExecuteDelete(SqliteConnection connection, SqliteTransaction transaction, string sql, long cutoff)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        return command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static object ToDbValue(long? value) => value.HasValue ? value.Value : DBNull.Value;

    private static long? ToMicroUsd(decimal? amount)
    {
        if (!amount.HasValue)
        {
            return null;
        }

        if (amount.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        return decimal.ToInt64(decimal.Round(amount.Value * 1_000_000m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal? FromMicroUsd(long? amount) => amount.HasValue ? amount.Value / 1_000_000m : null;

    private static long? ReadScalarNullableLong(SqliteCommand command)
    {
        var value = command.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static AiAnalysis ReadAiAnalysis(SqliteDataReader reader) => new(
        new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        ReadNullableString(reader, 6),
        Snapshot: null,
        CorrelationId: reader.GetString(0),
        Origin: reader.GetString(7),
        InformationalSchedule: ReadNullableString(reader, 8));

    private static long? ReadNullableLong(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static int? ReadNullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long EstimateBytes(ActivitySample sample, string? attributesJson)
    {
        return FixedEstimatedRowBytes
            + Encoding.UTF8.GetByteCount(sample.State)
            + Encoding.UTF8.GetByteCount(sample.ProcessName)
            + Encoding.UTF8.GetByteCount(sample.Application)
            + Encoding.UTF8.GetByteCount(sample.Context)
            + Encoding.UTF8.GetByteCount(sample.WindowTitle)
            + Encoding.UTF8.GetByteCount(sample.InstallationId)
            + (attributesJson is null ? 0 : Encoding.UTF8.GetByteCount(attributesJson));
    }

    private const string ActivitySchemaSql = """
        CREATE TABLE activity_samples (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sample_id TEXT NOT NULL UNIQUE,
            timestamp_utc_ticks INTEGER NOT NULL,
            start_utc_ticks INTEGER NOT NULL,
            timestamp_offset_minutes INTEGER NOT NULL,
            duration_seconds INTEGER NOT NULL CHECK (duration_seconds > 0),
            state TEXT NOT NULL,
            process_name TEXT NOT NULL,
            application TEXT NOT NULL,
            context TEXT NOT NULL,
            window_title TEXT NOT NULL,
            installation_id TEXT NOT NULL,
            key_presses INTEGER NOT NULL CHECK (key_presses >= 0),
            mouse_clicks INTEGER NOT NULL CHECK (mouse_clicks >= 0),
            attributes_json TEXT NULL,
            estimated_bytes INTEGER NOT NULL CHECK (estimated_bytes > 0)
        );
        CREATE INDEX ix_activity_samples_start ON activity_samples (start_utc_ticks, timestamp_utc_ticks);
        CREATE INDEX ix_activity_samples_timestamp ON activity_samples (timestamp_utc_ticks);
        """;

    private const string AiSchemaSql = """
        CREATE TABLE ai_request_usage (
            attempt_id TEXT PRIMARY KEY,
            correlation_id TEXT NOT NULL,
            occurred_utc_ticks INTEGER NOT NULL,
            completed_utc_ticks INTEGER NULL,
            origin TEXT NOT NULL,
            request_kind TEXT NOT NULL,
            provider TEXT NOT NULL,
            endpoint_host TEXT NOT NULL,
            requested_model TEXT NOT NULL,
            returned_model TEXT NULL,
            provider_response_id TEXT NULL,
            provider_request_id TEXT NULL,
            http_status INTEGER NULL CHECK (http_status BETWEEN 100 AND 599),
            elapsed_ms INTEGER NULL CHECK (elapsed_ms >= 0),
            provider_processing_ms INTEGER NULL CHECK (provider_processing_ms >= 0),
            image_count INTEGER NOT NULL CHECK (image_count >= 0),
            prompt_characters INTEGER NOT NULL CHECK (prompt_characters >= 0),
            max_output_tokens INTEGER NOT NULL CHECK (max_output_tokens >= 0),
            input_tokens INTEGER NULL CHECK (input_tokens >= 0),
            output_tokens INTEGER NULL CHECK (output_tokens >= 0),
            total_tokens INTEGER NULL CHECK (total_tokens >= 0),
            cached_input_tokens INTEGER NULL CHECK (cached_input_tokens >= 0),
            cache_write_tokens INTEGER NULL CHECK (cache_write_tokens >= 0),
            cache_creation_input_tokens INTEGER NULL CHECK (cache_creation_input_tokens >= 0),
            cache_read_input_tokens INTEGER NULL CHECK (cache_read_input_tokens >= 0),
            reasoning_tokens INTEGER NULL CHECK (reasoning_tokens >= 0),
            thinking_tokens INTEGER NULL CHECK (thinking_tokens >= 0),
            reported_cost_microusd INTEGER NULL CHECK (reported_cost_microusd >= 0),
            reported_upstream_cost_microusd INTEGER NULL CHECK (reported_upstream_cost_microusd >= 0),
            cost_source TEXT NOT NULL CHECK (cost_source IN ('provider', 'unavailable')),
            finish_reason TEXT NULL,
            success INTEGER NOT NULL CHECK (success IN (0, 1)),
            failure_code TEXT NULL
        );
        CREATE INDEX ix_ai_request_usage_occurred ON ai_request_usage (occurred_utc_ticks);
        CREATE INDEX ix_ai_request_usage_correlation ON ai_request_usage (correlation_id, occurred_utc_ticks);

        CREATE TABLE ai_analysis_results (
            correlation_id TEXT PRIMARY KEY,
            attempt_id TEXT NOT NULL UNIQUE,
            timestamp_utc_ticks INTEGER NOT NULL,
            snapshot_utc_ticks INTEGER NULL,
            application TEXT NOT NULL,
            context TEXT NOT NULL,
            summary TEXT NOT NULL,
            installation_id TEXT NOT NULL,
            origin TEXT NOT NULL,
            informational_schedule TEXT NULL,
            screenshot_paths TEXT NULL,
            image_count INTEGER NOT NULL CHECK (image_count >= 0),
            FOREIGN KEY (attempt_id) REFERENCES ai_request_usage(attempt_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_ai_analysis_results_timestamp ON ai_analysis_results (timestamp_utc_ticks);

        -- FTS provides local keyword candidate retrieval; synonym and typo resolution remains an application-level future concern.
        CREATE VIRTUAL TABLE ai_analysis_search USING fts5(
            correlation_id UNINDEXED,
            application,
            context,
            summary,
            tokenize = 'unicode61 remove_diacritics 2'
        );
        """;

    private const string ScreenshotTextSchemaSql = """
        CREATE TABLE screenshot_text_snapshots (
            artifact_identity TEXT NOT NULL PRIMARY KEY,
            capture_id TEXT NOT NULL,
            source_path TEXT NOT NULL,
            extracted_utc_ticks INTEGER NOT NULL,
            snapshot_json TEXT NOT NULL,
            updated_utc_ticks INTEGER NOT NULL
        );
        CREATE INDEX ix_screenshot_text_snapshots_capture
            ON screenshot_text_snapshots (capture_id, artifact_identity);
        """;

    private const string AiPricingSchemaSql = """
        CREATE TABLE ai_model_pricing (
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            service_tier TEXT NOT NULL,
            context_window TEXT NOT NULL,
            currency TEXT NOT NULL CHECK (currency = 'usd'),
            input_microusd_per_million INTEGER NOT NULL CHECK (input_microusd_per_million >= 0),
            cached_input_microusd_per_million INTEGER NULL CHECK (cached_input_microusd_per_million >= 0),
            cache_write_microusd_per_million INTEGER NULL CHECK (cache_write_microusd_per_million >= 0),
            output_microusd_per_million INTEGER NOT NULL CHECK (output_microusd_per_million >= 0),
            source_url TEXT NOT NULL,
            source_retrieved_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (provider, model, service_tier, context_window)
        );
        """;

    private const string CaptureHardwareSnapshotSchemaSql = """
        CREATE TABLE capture_hardware_snapshots (
            capture_id TEXT NOT NULL PRIMARY KEY REFERENCES screenshot_captures(capture_id) ON DELETE CASCADE,
            sampled_utc_ticks INTEGER NOT NULL,
            snapshot_json TEXT NOT NULL
        );
        CREATE INDEX ix_capture_hardware_snapshots_sampled
            ON capture_hardware_snapshots (sampled_utc_ticks, capture_id);
        """;

    private const string ScreenshotIntervalTelemetrySchemaSql = """
        CREATE TABLE screenshot_interval_telemetry (
            artifact_identity TEXT NOT NULL PRIMARY KEY,
            capture_id TEXT NOT NULL,
            interval_started_utc_ticks INTEGER NOT NULL,
            captured_utc_ticks INTEGER NOT NULL CHECK (captured_utc_ticks > interval_started_utc_ticks),
            cpu_usage_percent INTEGER NULL CHECK (cpu_usage_percent BETWEEN 0 AND 100),
            gpu_usage_percent INTEGER NULL CHECK (gpu_usage_percent BETWEEN 0 AND 100),
            updated_utc_ticks INTEGER NOT NULL
        );
        CREATE INDEX ix_screenshot_interval_telemetry_capture
            ON screenshot_interval_telemetry (capture_id, artifact_identity);
        CREATE INDEX ix_screenshot_interval_telemetry_captured
            ON screenshot_interval_telemetry (captured_utc_ticks, capture_id, artifact_identity);
        """;

    private const string AiReprocessingSchemaSql = """
        CREATE TABLE ai_analysis_artifacts (
            artifact_identity TEXT NOT NULL PRIMARY KEY,
            capture_id TEXT NOT NULL,
            correlation_id TEXT NOT NULL,
            FOREIGN KEY (correlation_id) REFERENCES ai_analysis_results(correlation_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_ai_analysis_artifacts_capture
            ON ai_analysis_artifacts (capture_id, artifact_identity);

        CREATE TABLE ai_reprocess_jobs (
            job_id TEXT NOT NULL PRIMARY KEY,
            created_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            range_start_utc_ticks INTEGER NOT NULL,
            range_end_utc_ticks INTEGER NOT NULL CHECK (range_end_utc_ticks > range_start_utc_ticks),
            selected_local_date TEXT NOT NULL CHECK (
                selected_local_date GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'),
            capture_origin TEXT NULL,
            configuration_fingerprint TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN (
                'pending', 'running', 'pause_requested', 'paused_by_user', 'paused_daily_quota',
                'completed', 'completed_with_errors', 'failed')),
            active_slot INTEGER NULL CHECK (active_slot IS NULL OR active_slot = 1),
            total_captures INTEGER NOT NULL CHECK (total_captures > 0),
            total_screenshots INTEGER NOT NULL CHECK (total_screenshots > 0),
            pause_reason TEXT NULL
        );
        CREATE UNIQUE INDEX ux_ai_reprocess_jobs_active_slot
            ON ai_reprocess_jobs (active_slot);

        CREATE TABLE ai_reprocess_job_items (
            job_id TEXT NOT NULL,
            capture_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            captured_utc_ticks INTEGER NOT NULL,
            capture_origin TEXT NOT NULL,
            artifact_identities_json TEXT NOT NULL,
            screenshot_count INTEGER NOT NULL CHECK (screenshot_count > 0),
            state TEXT NOT NULL CHECK (state IN ('pending', 'running', 'succeeded', 'skipped', 'failed')),
            attempt_count INTEGER NOT NULL CHECK (attempt_count >= 0),
            last_code TEXT NULL,
            updated_utc_ticks INTEGER NOT NULL,
            PRIMARY KEY (job_id, capture_id),
            UNIQUE (job_id, ordinal),
            FOREIGN KEY (job_id) REFERENCES ai_reprocess_jobs(job_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_ai_reprocess_job_items_next
            ON ai_reprocess_job_items (job_id, state, ordinal);
        """;

    private const string InstallationArchiveSchemaSql = """
        CREATE TABLE installation_profiles (
            installation_id TEXT NOT NULL PRIMARY KEY,
            machine_name TEXT NOT NULL,
            friendly_name TEXT NOT NULL,
            color TEXT NOT NULL,
            icon TEXT NOT NULL,
            first_seen_utc_ticks INTEGER NOT NULL,
            updated_utc_ticks INTEGER NOT NULL CHECK (updated_utc_ticks >= first_seen_utc_ticks),
            profile_revision INTEGER NOT NULL CHECK (profile_revision > 0)
        );

        CREATE TABLE screenshot_captures (
            capture_id TEXT NOT NULL PRIMARY KEY,
            installation_id TEXT NOT NULL,
            captured_utc_ticks INTEGER NOT NULL,
            origin TEXT NOT NULL,
            FOREIGN KEY (installation_id) REFERENCES installation_profiles(installation_id)
        );
        CREATE INDEX ix_screenshot_captures_installation
            ON screenshot_captures (installation_id, captured_utc_ticks, capture_id);
        CREATE INDEX ix_screenshot_captures_captured
            ON screenshot_captures (captured_utc_ticks, capture_id);

        CREATE TABLE archive_imports (
            archive_id TEXT NOT NULL PRIMARY KEY,
            archive_fingerprint TEXT NOT NULL,
            imported_utc_ticks INTEGER NOT NULL
        );

        CREATE TABLE store_metadata (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

    private const string SearchRevisionSchemaSql = """
        CREATE TABLE search_change_log (
            revision INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,
            entity_id TEXT NOT NULL,
            operation TEXT NOT NULL CHECK (operation IN ('upsert', 'delete'))
        );

        CREATE TRIGGER tr_search_activity_insert AFTER INSERT ON activity_samples BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('activity', CAST(NEW.id AS TEXT), 'upsert');
        END;
        CREATE TRIGGER tr_search_activity_update AFTER UPDATE ON activity_samples BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('activity', CAST(NEW.id AS TEXT), 'upsert');
        END;
        CREATE TRIGGER tr_search_activity_delete AFTER DELETE ON activity_samples BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('activity', CAST(OLD.id AS TEXT), 'delete');
        END;

        CREATE TRIGGER tr_search_capture_insert AFTER INSERT ON screenshot_captures BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('capture', NEW.capture_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_capture_update AFTER UPDATE ON screenshot_captures BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('capture', NEW.capture_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_capture_delete AFTER DELETE ON screenshot_captures BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('capture', OLD.capture_id, 'delete');
        END;

        CREATE TRIGGER tr_search_text_insert AFTER INSERT ON screenshot_text_snapshots BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_text_update AFTER UPDATE ON screenshot_text_snapshots BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_text_delete AFTER DELETE ON screenshot_text_snapshots BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', OLD.artifact_identity, 'delete');
        END;

        CREATE TRIGGER tr_search_telemetry_insert AFTER INSERT ON screenshot_interval_telemetry BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_telemetry_update AFTER UPDATE ON screenshot_interval_telemetry BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_telemetry_delete AFTER DELETE ON screenshot_interval_telemetry BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', OLD.artifact_identity, 'delete');
        END;

        CREATE TRIGGER tr_search_analysis_insert AFTER INSERT ON ai_analysis_results BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('analysis', NEW.correlation_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_analysis_update AFTER UPDATE ON ai_analysis_results BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('analysis', NEW.correlation_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_analysis_delete AFTER DELETE ON ai_analysis_results BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('analysis', OLD.correlation_id, 'delete');
        END;

        CREATE TRIGGER tr_search_analysis_artifact_insert AFTER INSERT ON ai_analysis_artifacts BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_analysis_artifact_update AFTER UPDATE ON ai_analysis_artifacts BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', NEW.artifact_identity, 'upsert');
        END;
        CREATE TRIGGER tr_search_analysis_artifact_delete AFTER DELETE ON ai_analysis_artifacts BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('screenshot', OLD.artifact_identity, 'upsert');
        END;

        CREATE TRIGGER tr_search_profile_insert AFTER INSERT ON installation_profiles BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('rebuild', NEW.installation_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_profile_update AFTER UPDATE ON installation_profiles BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('rebuild', NEW.installation_id, 'upsert');
        END;
        CREATE TRIGGER tr_search_profile_delete AFTER DELETE ON installation_profiles BEGIN
            INSERT INTO search_change_log (kind, entity_id, operation) VALUES ('rebuild', OLD.installation_id, 'delete');
        END;
        """;

    private readonly record struct SchemaColumn(string Name, string Type, bool NotNull, int PrimaryKeyOrder);
}

/// <summary>Contains the immutable installation owner and acquisition facts for one retained screenshot capture.</summary>
internal sealed record ScreenshotCaptureProvenance(
    string CaptureId,
    InstallationProfile Installation,
    DateTimeOffset CapturedAt,
    string Origin);

/// <summary>One ordered durable mutation of the authoritative local search sources.</summary>
internal sealed record SearchSourceChange(long Revision, string Kind, string EntityId, string Operation);

/// <summary>Contains the immutable acquisition facts for one local screenshot capture.</summary>
internal sealed record ScreenshotCaptureRegistration(
    string CaptureId,
    DateTimeOffset CapturedAt,
    string Origin)
{
    /// <summary>Returns the canonical durable representation or rejects malformed retained-file metadata.</summary>
    internal ScreenshotCaptureRegistration Validate()
    {
        if (!Guid.TryParseExact(CaptureId, "N", out var parsedCaptureId) || CapturedAt == default)
        {
            throw new InvalidDataException("Retained screenshot capture provenance is invalid.");
        }

        return this with
        {
            CaptureId = parsedCaptureId.ToString("N"),
            CapturedAt = CapturedAt.ToUniversalTime(),
            Origin = ScreenshotCaptureOrigins.Validate(Origin)
        };
    }
}

/// <summary>Contains the minimal activity projection needed to build aggregate reports.</summary>
internal sealed record ReportSourceSample(
    DateTimeOffset Timestamp,
    int DurationSeconds,
    string State,
    string Application,
    long KeyPresses,
    long MouseClicks,
    string InstallationId);

/// <summary>Groups persisted screenshot artifact identities by their original capture.</summary>
internal sealed record AiReprocessCatalogRecord(
    string CaptureId,
    string? InstallationId,
    DateTimeOffset IntervalStartedAt,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> ArtifactIdentities,
    bool HasTelemetry,
    bool HasAiDescription);

/// <summary>Contains persisted metadata presence for a capture discovered from retained files.</summary>
internal sealed record AiReprocessCapturePersistenceState(
    string CaptureId,
    DateTimeOffset? IntervalStartedAt,
    DateTimeOffset? CapturedAt,
    bool HasAiDescription);

/// <summary>Contains one locally materialized historical screenshot capture considered for AI reprocessing.</summary>
internal sealed record AiScreenshotReprocessCandidate(
    string CaptureId,
    string? InstallationId,
    DateTimeOffset CapturedAt,
    string CaptureOrigin,
    IReadOnlyList<string> ScreenshotPaths,
    IReadOnlyList<ScreenshotTextSnapshot> TextSnapshots,
    AnalysisContextSnapshot? HistoricalContext,
    bool HasAiDescription,
    int MissingFileCount,
    string ProcessName = "");

/// <summary>Represents the SQLite projection of one durable AI screenshot reprocessing job.</summary>
internal sealed record AiReprocessJobRecord(
    Guid JobId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateOnly SelectedDate,
    string? CaptureOrigin,
    string ConfigurationFingerprint,
    string State,
    int TotalCaptures,
    int TotalScreenshots,
    string? PauseReason);

/// <summary>Represents the SQLite projection of one durable AI screenshot reprocessing work item.</summary>
internal sealed record AiReprocessJobItemRecord(
    Guid JobId,
    string CaptureId,
    int Ordinal,
    DateTimeOffset CapturedAt,
    string CaptureOrigin,
    IReadOnlyList<string> ArtifactIdentities,
    int ScreenshotCount,
    string State,
    int AttemptCount,
    string? LastCode,
    DateTimeOffset UpdatedAt);
