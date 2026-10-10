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


using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WorkTrail.Services;

/// <summary>Durable intent makes filesystem/SQLite/index deletion retryable across errors and restarts.</summary>
internal sealed class ScreenshotDeletionJournal(LocalStore store)
{
    private string JournalDirectory => Path.Combine(store.DataDirectory, "pending-screenshot-deletions");

    internal sealed record Plan(string ScreenshotPath, string[] Artifacts, bool DeleteAnalysis, string AuthorizedRoot);

    /// <summary>Tracks remaining artifacts with one archive enumeration per authorized root.</summary>
    internal sealed class Batch
    {
        private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _roots = new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, HashSet<string>> Inventory(string root, CancellationToken cancellationToken)
        {
            if (_roots.TryGetValue(root, out var inventory)) return inventory;
            inventory = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var path in ScreenshotStorageLayout.EnumerateOwnedArtifacts(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var captureId = Path.GetFileName(path)[..32];
                if (!inventory.TryGetValue(captureId, out var artifacts))
                    inventory.Add(captureId, artifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                artifacts.Add(Path.GetFullPath(path));
            }
            _roots.Add(root, inventory);
            return inventory;
        }
    }

    // path identifies one monitor or window artifact to remove.
    // deleteAnalysis controls removal of its derived AI records.
    // batch supplies the already enumerated archive for bulk cleanup.
    // cancellationToken stops archive enumeration before a new deletion starts.
    internal Plan? Begin(string path, bool deleteAnalysis, Batch? batch = null, CancellationToken cancellationToken = default)
    {
        if (!ScreenCaptureService.IsOwnedArtifact(path) || !Path.IsPathFullyQualified(path)) return null;
        var journal = GetPath(path);
        if (File.Exists(journal))
        {
            var pending = Read(journal);
            if (deleteAnalysis && !pending.DeleteAnalysis)
            {
                pending = pending with { DeleteAnalysis = true };
                Save(journal, pending);
            }
            return pending;
        }
        var root = ScreenshotStorageLayout.NormalizeRoot(store.LoadSettings().ScreenshotDirectory);
        IReadOnlyList<string> artifacts;
        if (batch is null) artifacts = store.FindScreenshotArtifacts(path);
        else
        {
            var inventory = batch.Inventory(root, cancellationToken);
            var fullPath = Path.GetFullPath(path);
            var identity = LocalStore.ScreenshotIdentity(Path.GetFileName(path));
            artifacts = inventory.TryGetValue(Path.GetFileName(path)[..32], out var paths)
                ? paths.Where(candidate => string.Equals(Path.GetDirectoryName(candidate), Path.GetDirectoryName(fullPath), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(LocalStore.ScreenshotIdentity(Path.GetFileName(candidate)), identity, StringComparison.OrdinalIgnoreCase)).ToArray()
                : [];
        }
        if (artifacts.Count == 0) return null;
        var plan = new Plan(Path.GetFullPath(path), artifacts.ToArray(), deleteAnalysis,
            root);
        Validate(plan);
        Save(journal, plan);
        return plan;
    }

    // reportFailure receives isolated recovery errors; their durable intents remain available for retry.
    internal IReadOnlyList<Plan> Pending(Action<Exception>? reportFailure = null)
    {
        RejectLinks(JournalDirectory);
        if (!Directory.Exists(JournalDirectory)) return [];
        var plans = new List<Plan>();
        foreach (var path in Directory.EnumerateFiles(JournalDirectory, "*.json"))
        {
            try { plans.Add(Read(path)); }
            catch (Exception exception) when (reportFailure is not null
                && exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                reportFailure(exception);
            }
        }
        return plans;
    }

    // plan retains the root authorized when deletion began.
    // batch shares physical-artifact inventory across a cleanup or recovery run.
    // cancellationToken stops between individual file removals without losing intent.
    internal void Execute(Plan plan, Batch? batch = null, CancellationToken cancellationToken = default)
    {
        Validate(plan);
        var inventory = (batch ?? new Batch()).Inventory(plan.AuthorizedRoot, cancellationToken);
        // Intent is already durable. Any failure leaves it available even when the image is now absent.
        foreach (var path in plan.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
            var captureId = Path.GetFileName(path)[..32];
            if (inventory.TryGetValue(captureId, out var artifacts))
            {
                artifacts.Remove(Path.GetFullPath(path));
                if (artifacts.Count == 0) inventory.Remove(captureId);
            }
        }
        if (plan.DeleteAnalysis)
            foreach (var path in plan.Artifacts.Append(plan.ScreenshotPath).Distinct(StringComparer.OrdinalIgnoreCase))
                store.DeleteAiAnalysesReferencingScreenshot(path);
        store.DeleteScreenshotTextSnapshot(plan.ScreenshotPath);
        store.DeleteScreenshotIntervalTelemetry(plan.ScreenshotPath);
        store.DeleteScreenshotCaptureIfOrphaned(plan.ScreenshotPath,
            inventory.ContainsKey(Path.GetFileName(plan.ScreenshotPath)[..32]));
    }

    internal void Complete(Plan plan) => File.Delete(GetPath(plan.ScreenshotPath));

    private Plan Read(string path)
    {
        RejectLinks(path);
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Screenshot deletion intent is invalid.");
        Validate(plan);
        if (!string.Equals(Path.GetFullPath(path), GetPath(plan.ScreenshotPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Screenshot deletion identity does not match its journal.");
        return plan;
    }

    // plan must contain explicit persisted identity and root before path helpers can inspect it.
    private void Validate(Plan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.ScreenshotPath) || string.IsNullOrWhiteSpace(plan.AuthorizedRoot)
            || plan.Artifacts is null || plan.Artifacts.Length == 0)
            throw new InvalidDataException("Screenshot deletion has no valid identity, root, or artifacts.");
        var root = ScreenshotStorageLayout.NormalizeRoot(plan.AuthorizedRoot);
        RejectLinks(root);
        var identity = LocalStore.ScreenshotIdentity(Path.GetFileName(plan.ScreenshotPath));
        foreach (var path in plan.Artifacts.Append(plan.ScreenshotPath))
        {
            if (!Path.IsPathFullyQualified(path) || !ScreenCaptureService.IsOwnedArtifact(path)
                || !ScreenshotStorageLayout.IsSameOrDescendant(Path.GetDirectoryName(Path.GetFullPath(path))!, root)
                || !string.Equals(identity, LocalStore.ScreenshotIdentity(Path.GetFileName(path)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Screenshot deletion escaped the configured capture identity or root.");
            RejectLinks(path);
        }
    }

    private static void RejectLinks(string path)
    {
        ScreenshotStorageLayout.RejectLinks(path);
    }

    private string GetPath(string path)
    {
        var identityPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, LocalStore.ScreenshotIdentity(Path.GetFileName(path)));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityPath.ToUpperInvariant())));
        return Path.GetFullPath(Path.Combine(JournalDirectory, key + ".json"));
    }

    private void Save(string path, Plan plan)
    {
        RejectLinks(JournalDirectory);
        Directory.CreateDirectory(JournalDirectory);
        var temporary = path + ".tmp";
        RejectLinks(temporary);
        // Flush intent before removing any artifact. Atomic replacement keeps crash recovery deterministic.
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, plan);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
