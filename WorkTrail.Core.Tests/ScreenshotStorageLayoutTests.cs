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
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class ScreenshotStorageLayoutTests
{
    [Fact]
    public void GetDayDirectory_UsesMonthIsoWeekAndDaySegments()
    {
        var root = CreateAbsoluteRoot();

        var directory = ScreenshotStorageLayout.GetDayDirectory(root, new DateOnly(2026, 8, 23));

        Assert.Equal(
            Path.Combine(root, "2026-08", "week-2026-34", "2026-08-23"),
            directory);
    }

    [Fact]
    public void GetDayDirectory_UsesIsoWeekYearAtCalendarYearBoundary()
    {
        var root = CreateAbsoluteRoot();

        var directory = ScreenshotStorageLayout.GetDayDirectory(root, new DateOnly(2021, 1, 1));

        Assert.Equal(
            Path.Combine(root, "2021-01", "week-2020-53", "2021-01-01"),
            directory);
    }

    [Fact]
    public void NormalizeRoot_PreservesAWindowsDriveRoot()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())
            ?? throw new InvalidOperationException("The test drive root is unavailable.");

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(driveRoot)),
            ScreenshotStorageLayout.NormalizeRoot(driveRoot),
            ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("screenshots")]
    public void GetDayDirectory_RejectsNonAbsoluteRoot(string root)
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenshotStorageLayout.GetDayDirectory(root, new DateOnly(2026, 8, 23)));
    }

    [Fact]
    public void TryGetDay_AcceptsOnlyAnArtifactDirectlyInsideItsCanonicalLeaf()
    {
        var root = CreateAbsoluteRoot();
        var day = new DateOnly(2021, 1, 1);
        var canonicalPath = Path.Combine(
            ScreenshotStorageLayout.GetDayDirectory(root, day),
            $"{Guid.NewGuid():N}_1.0.0_manual_monitor-1.webp");
        var wrongWeekPath = Path.Combine(
            root,
            "2021-01",
            "week-2021-01",
            "2021-01-01",
            Path.GetFileName(canonicalPath));
        var nestedPath = Path.Combine(
            ScreenshotStorageLayout.GetDayDirectory(root, day),
            "extra",
            Path.GetFileName(canonicalPath));

        Assert.True(ScreenshotStorageLayout.TryGetDay(root, canonicalPath, out var parsedDay));
        Assert.Equal(day, parsedDay);
        Assert.Equal(day, ScreenshotStorageLayout.GetDay(root, canonicalPath));
        Assert.False(ScreenshotStorageLayout.TryGetDay(root, wrongWeekPath, out _));
        Assert.False(ScreenshotStorageLayout.TryGetDay(root, nestedPath, out _));
        Assert.False(ScreenshotStorageLayout.TryGetDay(root, "relative.webp", out _));
        Assert.Throws<InvalidDataException>(() => ScreenshotStorageLayout.GetDay(root, wrongWeekPath));
    }

    [Fact]
    public void EnumerateOwnedArtifacts_IsRecursiveAndExcludesUnownedFiles()
    {
        var root = CreateAbsoluteRoot();
        var nestedDirectory = Path.Combine(root, "2026-08", "week-2026-34", "2026-08-23");
        Directory.CreateDirectory(nestedDirectory);
        var rootArtifact = CreateOwnedArtifact(root, Guid.NewGuid(), "monitor-1");
        var nestedArtifact = CreateOwnedArtifact(nestedDirectory, Guid.NewGuid(), "active-window");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "not a screenshot");
        File.WriteAllText(Path.Combine(nestedDirectory, "image.webp"), "not owned");

        try
        {
            var artifacts = ScreenshotStorageLayout.EnumerateOwnedArtifacts(root)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(
                new[] { rootArtifact, nestedArtifact }.Order(StringComparer.OrdinalIgnoreCase),
                artifacts,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateAbsoluteRoot() =>
        Path.Combine(Path.GetTempPath(), $"WorkTrail-storage-layout-{Guid.NewGuid():N}");

    private static string CreateOwnedArtifact(
        string directory,
        Guid captureId,
        string target,
        bool raw = false,
        DateTimeOffset? capturedAt = null)
    {
        Directory.CreateDirectory(directory);
        var rawSuffix = raw ? "-raw" : string.Empty;
        var path = Path.Combine(
            directory,
            $"{captureId:N}_1.0.0_manual_{target}{rawSuffix}.webp");
        File.WriteAllText(path, "source");
        if (capturedAt is { } timestamp)
        {
            File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
        }

        return path;
    }
}
