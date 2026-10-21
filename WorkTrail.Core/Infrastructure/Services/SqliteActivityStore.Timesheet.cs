// SPDX-License-Identifier: MIT
using System.Text.Json;
using WorkTrail.Application;

namespace WorkTrail.Services;

internal sealed partial class SqliteActivityStore
{
    private const string TimesheetEvidenceSchemaSql = """
        CREATE TABLE timesheet_capture_context (
            capture_id TEXT NOT NULL PRIMARY KEY,
            interval_seconds INTEGER NOT NULL,
            project_hint TEXT NOT NULL
        );
        CREATE TABLE timesheet_checkpoints (
            attempt_id TEXT NOT NULL PRIMARY KEY,
            installation_id TEXT NOT NULL,
            observed_utc_ticks INTEGER NOT NULL,
            source_capture_id TEXT NOT NULL,
            observation_json TEXT NOT NULL
        );
        CREATE INDEX ix_timesheet_checkpoint_time ON timesheet_checkpoints(observed_utc_ticks);
        CREATE TABLE timesheet_session_events (
            event_id TEXT NOT NULL PRIMARY KEY,
            installation_id TEXT NOT NULL,
            observed_utc_ticks INTEGER NOT NULL,
            event_json TEXT NOT NULL
        );
        CREATE INDEX ix_timesheet_session_time ON timesheet_session_events(installation_id, observed_utc_ticks);
        CREATE TRIGGER tr_timesheet_capture_deleted AFTER DELETE ON screenshot_captures
        BEGIN
            DELETE FROM timesheet_checkpoints WHERE source_capture_id = OLD.capture_id;
            DELETE FROM timesheet_capture_context WHERE capture_id = OLD.capture_id;
        END;
        CREATE TRIGGER tr_timesheet_analysis_deleted AFTER DELETE ON ai_analysis_results
        BEGIN
            DELETE FROM timesheet_checkpoints WHERE source_capture_id = OLD.correlation_id;
        END;
        """;

    internal void SaveTimesheetCaptureContext(string captureId, int seconds, string project)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO timesheet_capture_context VALUES ($id, $seconds, $project) ON CONFLICT(capture_id) DO NOTHING";
        command.Parameters.AddWithValue("$id", captureId);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.Parameters.AddWithValue("$project", project);
        command.ExecuteNonQuery();
    }

    internal Dictionary<string, (int Seconds, string Project)> ReadTimesheetCaptureContexts(DateTimeOffset from, DateTimeOffset to)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.capture_id, t.interval_seconds, t.project_hint FROM timesheet_capture_context t
            JOIN screenshot_captures c ON c.capture_id = t.capture_id
            WHERE c.captured_utc_ticks >= $from AND c.captured_utc_ticks < $to
            """;
        command.Parameters.AddWithValue("$from", from.UtcTicks);
        command.Parameters.AddWithValue("$to", to.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        while (reader.Read()) result.Add(reader.GetString(0), (reader.GetInt32(1), reader.GetString(2)));
        return result;
    }

    // The comparison owner supplies a proven compatible artifact. Missing analysis stays missing evidence.
    internal TimesheetObservation? CreateCheckpoint(string attempt, string installation, DateTimeOffset observed,
        string path, string application, string? title, string project, int seconds)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.capture_id, r.correlation_id, r.summary
            FROM ai_analysis_artifacts a
            JOIN ai_analysis_results r ON r.correlation_id = a.correlation_id
            JOIN screenshot_captures c ON c.capture_id = a.capture_id
            WHERE a.artifact_identity = $artifact AND c.installation_id = $installation
              AND c.captured_utc_ticks <= $at AND r.summary <> ''
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$artifact", ArtifactIdentityFromScreenshotPath(path));
        command.Parameters.AddWithValue("$installation", installation);
        command.Parameters.AddWithValue("$at", observed.UtcTicks);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new("obs-" + attempt, installation, observed.ToUniversalTime(), reader.GetString(0), reader.GetString(1),
            "unchanged_checkpoint", reader.GetString(2), application, title, project, seconds);
    }

    internal void SaveTimesheetCheckpoint(TimesheetObservation observation)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO timesheet_checkpoints VALUES ($id, $installation, $at, $source, $json) ON CONFLICT(attempt_id) DO NOTHING";
        command.Parameters.AddWithValue("$id", observation.Id);
        command.Parameters.AddWithValue("$installation", observation.InstallationId);
        command.Parameters.AddWithValue("$at", observation.ObservedAt.UtcTicks);
        command.Parameters.AddWithValue("$source", observation.CaptureId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(observation, _json));
        command.ExecuteNonQuery();
    }

    internal List<TimesheetObservation> ReadTimesheetCheckpoints(DateTimeOffset from, DateTimeOffset to)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        // Deleted source descriptions cannot be resurrected through a retained checkpoint.
        command.CommandText = """
            SELECT p.observation_json FROM timesheet_checkpoints p
            JOIN screenshot_captures c ON c.capture_id = p.source_capture_id
            JOIN ai_analysis_results a ON a.correlation_id = p.source_capture_id
            WHERE p.observed_utc_ticks >= $from AND p.observed_utc_ticks < $to
            ORDER BY p.observed_utc_ticks, p.attempt_id
            """;
        command.Parameters.AddWithValue("$from", from.UtcTicks);
        command.Parameters.AddWithValue("$to", to.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new List<TimesheetObservation>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<TimesheetObservation>(reader.GetString(0), _json)
            ?? throw new InvalidDataException("Invalid timesheet checkpoint."));
        return result;
    }

    internal void SaveTimesheetSessionEvent(TimesheetSessionEvent signal)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO timesheet_session_events VALUES ($id, $installation, $at, $json) ON CONFLICT(event_id) DO NOTHING";
        command.Parameters.AddWithValue("$id", signal.Id);
        command.Parameters.AddWithValue("$installation", signal.InstallationId);
        command.Parameters.AddWithValue("$at", signal.At.UtcTicks);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(signal, _json));
        command.ExecuteNonQuery();
    }

    internal List<TimesheetSessionEvent> ReadTimesheetSessionEvents(DateTimeOffset from, DateTimeOffset to)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json FROM timesheet_session_events e WHERE observed_utc_ticks < $to
            AND observed_utc_ticks >= COALESCE((
                SELECT MAX(p.observed_utc_ticks) FROM timesheet_session_events p
                WHERE p.installation_id = e.installation_id AND p.observed_utc_ticks <= $from
                AND json_extract(p.event_json, '$.kind') = 'runtime_start'), 0)
            ORDER BY observed_utc_ticks, event_id
            """;
        command.Parameters.AddWithValue("$from", from.UtcTicks);
        command.Parameters.AddWithValue("$to", to.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new List<TimesheetSessionEvent>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<TimesheetSessionEvent>(reader.GetString(0), _json)
            ?? throw new InvalidDataException("Invalid timesheet session event."));
        return result;
    }
}
