// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Text.Json;

namespace WorkTrail.Services;

/// <summary>Publishes complete image files only after their durable installation provenance exists.</summary>
internal sealed class ScreenshotPublicationJournal(LocalStore store)
{
    private string JournalDirectory => Path.Combine(store.DataDirectory, "pending-screenshot-publications");
    internal string TransientRoot => Path.Combine(store.DataDirectory, "transient-screenshots");
    internal sealed record Plan(string CaptureId, string InstallationId, DateTimeOffset CapturedAt,
        string Origin, string AuthorizedRoot, string[] Artifacts, bool Keep);

    internal static string StagingPath(string finalPath) => finalPath + ".pending";

    // capture contains the completed worker's canonical destinations and immutable capture metadata.
    // authorizedRoot is frozen before the capture worker starts.
    // installationId identifies the installation that requested the capture.
    // keep determines whether interrupted work is completed or discarded on restart.
    internal void Publish(ScreenshotCaptureResult capture, string authorizedRoot, string installationId, bool keep)
    {
        if (capture.AllScreenshotPaths.Count == 0) return;
        var first = capture.StoredScreenshotPaths.FirstOrDefault() ?? capture.AllScreenshotPaths[0];
        var capturedAt = capture.CapturedAt ?? new DateTimeOffset(
            File.GetLastWriteTimeUtc(File.Exists(StagingPath(first)) ? StagingPath(first) : first), TimeSpan.Zero);
        var plan = new Plan(capture.CaptureId, installationId, capturedAt, capture.CaptureOrigin,
            ScreenshotStorageLayout.NormalizeRoot(authorizedRoot), capture.AllScreenshotPaths.ToArray(), keep);
        Validate(plan);
        Save(plan);
        Publish(plan);
        if (keep) Complete(plan);
    }

    // reportFailure receives isolated errors while preserving the corresponding durable intent.
    internal void Recover(Action<Exception> reportFailure)
    {
        ScreenshotStorageLayout.RejectLinks(JournalDirectory);
        if (!Directory.Exists(JournalDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(JournalDirectory, "*.json"))
        {
            try
            {
                var plan = Read(path);
                if (plan.Keep) { Publish(plan); Complete(plan); }
                else Discard(plan);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                reportFailure(exception);
            }
        }
    }

    // captureId identifies transient files whose intent must survive a failed removal.
    internal void Discard(string captureId)
    {
        var path = GetPath(captureId);
        if (!File.Exists(path)) return;
        var plan = Read(path);
        if (plan.Keep)
        {
            // Persist rollback before any removal so recovery cannot republish a failed partial capture.
            plan = plan with { Keep = false };
            Save(plan, overwrite: true);
        }
        Discard(plan);
    }

    // root limits cleanup to the root being maintained, including the private transient workspace.
    // cutoff applies the retention period, or is absent when startup owns the idle transient workspace.
    // cancellationToken interrupts the one directory enumeration and individual removals.
    internal void CleanupUnclaimedStages(string root, DateTimeOffset? cutoff, CancellationToken cancellationToken)
    {
        root = ScreenshotStorageLayout.NormalizeRoot(root);
        ScreenshotStorageLayout.RejectLinks(root);
        ScreenshotStorageLayout.RejectLinks(JournalDirectory);
        if (!Directory.Exists(root)) return;
        var claimed = Directory.Exists(JournalDirectory)
            ? Directory.EnumerateFiles(JournalDirectory, "*.json").Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var staged in Directory.EnumerateFiles(root, "*.pending", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalPath = staged[..^".pending".Length];
            if (!ScreenCaptureService.IsOwnedArtifact(finalPath)
                || !ScreenshotStorageLayout.TryGetDay(root, finalPath, out var day)
                || cutoff is { } expiresBefore && day >= DateOnly.FromDateTime(expiresBefore.LocalDateTime)
                || claimed.Contains(Path.GetFileName(finalPath)[..32])) continue;
            ScreenshotStorageLayout.RejectLinks(staged);
            File.Delete(staged);
        }
    }

    private void Publish(Plan plan)
    {
        Validate(plan);
        store.RegisterScreenshotCapture(plan.CaptureId, plan.InstallationId, plan.CapturedAt, plan.Origin);
        foreach (var path in plan.Artifacts)
        {
            var staged = StagingPath(path);
            if (File.Exists(staged)) File.Move(staged, path, overwrite: false);
            else if (!File.Exists(path)) throw new FileNotFoundException("A pending capture has no complete image.");
        }
    }

    private void Discard(Plan plan)
    {
        Validate(plan);
        foreach (var path in plan.Artifacts)
        {
            File.Delete(StagingPath(path));
            File.Delete(path);
            store.DeleteScreenshotTextSnapshot(path);
            store.DeleteScreenshotIntervalTelemetry(path);
        }
        store.DeleteScreenshotCaptureIfOrphaned(plan.Artifacts[0], hasPhysicalArtifact: false);
        Complete(plan);
    }

    private Plan Read(string path)
    {
        ScreenshotStorageLayout.RejectLinks(path);
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Screenshot publication intent is invalid.");
        Validate(plan);
        if (!string.Equals(Path.GetFullPath(path), GetPath(plan.CaptureId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Screenshot publication identity does not match its journal.");
        return plan;
    }

    private static void Validate(Plan plan)
    {
        if (!Guid.TryParseExact(plan.CaptureId, "N", out _) || !Guid.TryParseExact(plan.InstallationId, "N", out _)
            || plan.Artifacts is null || plan.Artifacts.Length == 0)
            throw new InvalidDataException("Screenshot publication has invalid identity or artifacts.");
        ScreenshotCaptureOrigins.Validate(plan.Origin);
        var root = ScreenshotStorageLayout.NormalizeRoot(plan.AuthorizedRoot);
        ScreenshotStorageLayout.RejectLinks(root);
        foreach (var path in plan.Artifacts)
        {
            if (!Path.IsPathFullyQualified(path) || !ScreenCaptureService.IsOwnedArtifact(path)
                || !ScreenshotStorageLayout.TryGetDay(root, path, out _)
                || !Path.GetFileName(path).StartsWith(plan.CaptureId + "_", StringComparison.Ordinal)
                || Path.GetFileName(path).Split('_')[2] != plan.Origin)
                throw new InvalidDataException("Screenshot publication escaped the authorized capture root or identity.");
            ScreenshotStorageLayout.RejectLinks(path);
            ScreenshotStorageLayout.RejectLinks(StagingPath(path));
        }
    }

    private string GetPath(string captureId)
    {
        if (!Guid.TryParseExact(captureId, "N", out _)) throw new InvalidDataException("Screenshot publication has an invalid capture identity.");
        return Path.Combine(JournalDirectory, captureId + ".json");
    }

    private void Complete(Plan plan) => File.Delete(GetPath(plan.CaptureId));

    private void Save(Plan plan, bool overwrite = false)
    {
        ScreenshotStorageLayout.RejectLinks(JournalDirectory);
        Directory.CreateDirectory(JournalDirectory);
        var path = GetPath(plan.CaptureId);
        var temporary = path + ".tmp";
        ScreenshotStorageLayout.RejectLinks(temporary);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, plan);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite);
    }
}
