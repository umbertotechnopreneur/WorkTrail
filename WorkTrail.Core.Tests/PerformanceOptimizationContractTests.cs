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


using System;
using System.IO;
using System.Linq;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Guards runtime ownership of cached settings and lightweight score telemetry.</summary>
public sealed class PerformanceOptimizationContractTests
{
    [Fact]
    public void RuntimeApplication_UsesSnapshotAfterConstructionAndPersistsBeforeReplacingIt()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Application", "WorkTrailApplication.cs"));
        var applicationStart = source.IndexOf("public sealed partial class WorkTrailApplication", StringComparison.Ordinal);
        Assert.True(applicationStart >= 0);
        var applicationSource = source[applicationStart..];

        Assert.DoesNotContain("_store.LoadSettings()", applicationSource, StringComparison.Ordinal);
        var persistStart = applicationSource.IndexOf("private void PersistSettings", StringComparison.Ordinal);
        var persistEnd = applicationSource.IndexOf("private PendingManualScreenshotState", persistStart, StringComparison.Ordinal);
        var persistSource = applicationSource[persistStart..persistEnd];
        Assert.True(
            persistSource.IndexOf("_store.SaveSettings(settings);", StringComparison.Ordinal)
            < persistSource.IndexOf("_settingsSnapshot.Replace(settings);", StringComparison.Ordinal));
    }

    [Fact]
    public void HardwareTelemetry_HasOneRuntimeOwnerAndNoLegacyReaders()
    {
        // Runtime ownership spans the facade's partial files after responsibility-based extraction.
        var source = string.Join(Environment.NewLine,
            Directory.GetFiles(RepositoryFile("WorkTrail.Core", "Application"), "WorkTrailApplication*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.Contains("IHardwareTelemetryService", source, StringComparison.Ordinal);
        Assert.Contains("_snapshot.CaptureAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementObjectSearcher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemSnapshotReuseWindow", source, StringComparison.Ordinal);
        Assert.False(File.Exists(RepositoryFile("WorkTrail.Core", "Infrastructure", "Services", "SystemUsageSampler.cs")));
        Assert.False(File.Exists(RepositoryFile("WorkTrail.Core", "Infrastructure", "Services", "SystemSnapshotService.cs")));
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
