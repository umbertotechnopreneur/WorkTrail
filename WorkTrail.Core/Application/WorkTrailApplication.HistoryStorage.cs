// SPDX-License-Identifier: MIT
using Microsoft.Extensions.Logging;
using WorkTrail.Services;

namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    private readonly object _historyStorageGate = new();
    private DateTimeOffset _historyStorageMeasuredAt;
    private string? _historyStorageScreenshotDirectory;
    private long _historyStorageBytes;

    private static OperationResult<RetentionPreview> InvalidHistoryRange() =>
        OperationResult<RetentionPreview>.Failure("retention.range.invalid", "HistoryRangeInvalid",
            new ValidationIssue("range", "invalid", "HistoryRangeInvalid"));

    private void InvalidateHistoryStorageMeasurement()
    {
        lock (_historyStorageGate) _historyStorageMeasuredAt = default;
    }

    // Cache directory traversal so progress polling does not repeatedly scan a large history.
    private long MeasureHistoryStorage(string screenshotDirectory)
    {
        lock (_historyStorageGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_historyStorageScreenshotDirectory == screenshotDirectory
                && now - _historyStorageMeasuredAt < TimeSpan.FromMinutes(1)) return _historyStorageBytes;
            try
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    _store.ActivityDatabasePath,
                    _store.ActivityDatabasePath + "-wal",
                    _store.ActivityDatabasePath + "-shm"
                };
                paths.UnionWith(ScreenshotStorageLayout.EnumerateOwnedArtifacts(screenshotDirectory));
                if (Directory.Exists(_store.SearchIndexRootDirectory))
                {
                    paths.UnionWith(Directory.EnumerateFiles(_store.SearchIndexRootDirectory, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = false,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }));
                }
                long total = 0;
                foreach (var path in paths)
                {
                    // Search files and SQLite sidecars can disappear during normal maintenance.
                    try
                    {
                        var info = new FileInfo(path);
                        if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                            total = checked(total + info.Length);
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }
                _historyStorageBytes = total;
                _historyStorageScreenshotDirectory = screenshotDirectory;
                _historyStorageMeasuredAt = now;
                return total;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Local history storage could not be measured.");
                throw;
            }
        }
    }
}
