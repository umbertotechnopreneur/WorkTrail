// SPDX-License-Identifier: MIT
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WorkTrail.Services;

internal sealed partial class SqliteActivityStore
{
    internal void DeleteScreenshotImage(string artifactIdentity)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM screenshot_images WHERE artifact_identity = $identity;";
        command.Parameters.AddWithValue("$identity", artifactIdentity);
        command.ExecuteNonQuery();
    }

    private void SaveScreenshotImages(SqliteConnection connection, SqliteTransaction transaction,
        string captureId, IReadOnlyList<ScreenshotImageMetadata>? images)
    {
        if (images is null) return;
        foreach (var image in images)
        {
            var identity = LocalStore.ScreenshotIdentity(Path.GetFileName(image.Path));
            if (!ScreenCaptureService.IsOwnedArtifact(image.Path)
                || !identity.StartsWith(captureId + "_", StringComparison.Ordinal)
                || image.ScreenIndex < 1 || image.ImageWidth <= 0 || image.ImageHeight <= 0
                || image.ScreenWidth <= 0 || image.ScreenHeight <= 0
                || string.IsNullOrWhiteSpace(image.ScreenName)
                || image.RelatedScreenIndices is null || image.RelatedScreenIndices.Count == 0
                || image.RelatedScreenIndices.Any(index => index < 1)
                || !image.RelatedScreenIndices.Contains(image.ScreenIndex)
                || image.Role is not ("active-window" or "secondary-window" or "monitor-context")
                || image.Priority is not ("primary" or "secondary" or "context")
                || image.Attention is not ("high" or "normal" or "low")
                || string.IsNullOrWhiteSpace(image.RuleReason)
                || image.MicroPromptIds?.Any(string.IsNullOrWhiteSpace) == true
                || (!image.ForAnalysis && !image.ForGallery))
                throw new InvalidDataException("Screenshot image metadata is invalid.");

            // Persist portable identity, never a machine-specific path. Publication recovery is idempotent.
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO screenshot_images (artifact_identity, capture_id, metadata_json)
                VALUES ($identity, $capture, $metadata)
                ON CONFLICT(artifact_identity) DO UPDATE SET metadata_json = excluded.metadata_json
                WHERE screenshot_images.capture_id = excluded.capture_id
                  AND screenshot_images.metadata_json = excluded.metadata_json;
                """;
            command.Parameters.AddWithValue("$identity", identity);
            command.Parameters.AddWithValue("$capture", captureId);
            command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(image with { Path = identity }, _json));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("Screenshot image metadata conflicts with its captured facts.");
        }
    }

    /// <summary>Restores image facts in requested order, rebinding portable identities to retained paths.</summary>
    internal IReadOnlyList<ScreenshotImageMetadata> LoadScreenshotImages(IEnumerable<string> paths)
    {
        var requested = paths.ToArray();
        var identities = requested.Select(path => LocalStore.ScreenshotIdentity(Path.GetFileName(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new Dictionary<string, ScreenshotImageMetadata>(StringComparer.OrdinalIgnoreCase);
        using var connection = OpenConnection();
        foreach (var batch in identities.Chunk(100))
        {
            using var command = connection.CreateCommand();
            var parameters = batch.Select((identity, index) =>
            {
                var name = "$image" + index;
                command.Parameters.AddWithValue(name, identity);
                return name;
            }).ToArray();
            command.CommandText = $"SELECT artifact_identity, metadata_json FROM screenshot_images WHERE artifact_identity IN ({string.Join(',', parameters)});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var metadata = JsonSerializer.Deserialize<ScreenshotImageMetadata>(reader.GetString(1), _json)
                    ?? throw new InvalidDataException("Screenshot image metadata is empty.");
                found.Add(reader.GetString(0), metadata);
            }
        }

        return requested.Where(path => found.ContainsKey(LocalStore.ScreenshotIdentity(Path.GetFileName(path))))
            .Select(path => found[LocalStore.ScreenshotIdentity(Path.GetFileName(path))] with { Path = path }).ToArray();
    }
}
