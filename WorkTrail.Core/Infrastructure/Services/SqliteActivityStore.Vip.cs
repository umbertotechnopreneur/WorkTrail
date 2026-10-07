// SPDX-License-Identifier: MIT

using WorkTrail.Application;
using Microsoft.Data.Sqlite;

namespace WorkTrail.Services;

internal sealed partial class SqliteActivityStore
{
    private const string VipScreenshotSchemaSql = """
        CREATE TABLE screenshot_vip_metadata (
            capture_id TEXT NOT NULL PRIMARY KEY,
            note TEXT NOT NULL DEFAULT '',
            updated_utc_ticks INTEGER NOT NULL,
            FOREIGN KEY (capture_id) REFERENCES screenshot_captures(capture_id) ON DELETE CASCADE
        );
        CREATE TRIGGER tr_screenshot_vip_delete AFTER DELETE ON screenshot_captures
        BEGIN
            DELETE FROM screenshot_vip_metadata WHERE capture_id = OLD.capture_id;
        END;
        """;

    /// <summary>Adds missing VIP objects to an extracted archive copy before its merge is examined.</summary>
    /// <param name="databasePath">The owned temporary database copy, never the source archive.</param>
    internal static void EnsureVipScreenshotSchema(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VipScreenshotSchemaSql
            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
            .Replace("CREATE TRIGGER ", "CREATE TRIGGER IF NOT EXISTS ", StringComparison.Ordinal);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Marks an already published manual capture as VIP without replacing a saved note.</summary>
    /// <param name="captureId">The durable capture identity shared by all monitors.</param>
    /// <exception cref="InvalidOperationException">The capture does not exist or is not manual.</exception>
    internal void RegisterVipScreenshot(string captureId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO screenshot_vip_metadata (capture_id, note, updated_utc_ticks)
            SELECT capture_id, '', $updated FROM screenshot_captures
            WHERE capture_id = $captureId AND origin = 'manual'
            ON CONFLICT(capture_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcTicks);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("A VIP capture requires a newly retained manual screenshot.");
        }
    }

    /// <summary>Updates a VIP note only while the underlying capture still exists.</summary>
    /// <param name="request">The validated capture identity and plain-text note.</param>
    internal bool SaveVipScreenshotNote(VipScreenshotNoteRequest request)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE screenshot_vip_metadata
            SET note = $note, updated_utc_ticks = MAX(updated_utc_ticks + 1, $updated)
            WHERE capture_id = $captureId
              AND EXISTS (SELECT 1 FROM screenshot_captures AS capture
                          WHERE capture.capture_id = $captureId AND capture.origin = 'manual');
            """;
        command.Parameters.AddWithValue("$captureId", request.CaptureId);
        command.Parameters.AddWithValue("$note", request.Note);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcTicks);
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>Finds candidate local VIP dates using the existing capture timestamp index.</summary>
    /// <param name="request">The inclusive local date range.</param>
    /// <param name="cancellationToken">Cancels reading the bounded capture set.</param>
    internal IReadOnlyList<DateOnly> GetVipScreenshotDates(VipScreenshotDatesRequest request, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
        var end = new DateTimeOffset(request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT capture.captured_utc_ticks FROM screenshot_captures AS capture
            JOIN screenshot_vip_metadata AS vip ON vip.capture_id = capture.capture_id
            WHERE capture.captured_utc_ticks >= $start AND capture.captured_utc_ticks < $end
            ORDER BY capture.captured_utc_ticks;
            """;
        command.Parameters.AddWithValue("$start", start.UtcTicks);
        command.Parameters.AddWithValue("$end", end.UtcTicks);
        using var reader = command.ExecuteReader();
        var dates = new SortedSet<DateOnly>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            dates.Add(DateOnly.FromDateTime(new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero).LocalDateTime));
        }
        return dates.ToArray();
    }
}
