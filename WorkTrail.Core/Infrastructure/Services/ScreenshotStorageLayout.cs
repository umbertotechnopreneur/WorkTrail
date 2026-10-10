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


using System.Globalization;

namespace WorkTrail.Services;

/// <summary>Defines and validates the calendar-based directory layout for WorkTrail screenshot artifacts.</summary>
internal static class ScreenshotStorageLayout
{
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>Resolves the year-month, ISO-week, and local-day directory for one capture.</summary>
    internal static string GetDayDirectory(string rootDirectory, DateTimeOffset capturedAt) =>
        GetDayDirectory(rootDirectory, DateOnly.FromDateTime(capturedAt.ToLocalTime().DateTime));

    /// <summary>Resolves the year-month, ISO-week, and local-day directory for one local date.</summary>
    internal static string GetDayDirectory(string rootDirectory, DateOnly date)
    {
        var root = NormalizeRoot(rootDirectory);
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var month = dateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var week = $"week-{ISOWeek.GetYear(dateTime):0000}-{ISOWeek.GetWeekOfYear(dateTime):00}";
        var day = dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Combine(root, month, week, day);
    }

    /// <summary>Reads the authoritative local day from an artifact stored directly in a canonical layout leaf.</summary>
    internal static DateOnly GetDay(string rootDirectory, string artifactPath)
    {
        if (TryGetDay(rootDirectory, artifactPath, out var day))
        {
            return day;
        }

        throw new InvalidDataException($"Screenshot artifact is not inside a canonical day directory: '{artifactPath}'.");
    }

    /// <summary>Tries to read the local day from a canonical year-month, ISO-week, and day artifact path.</summary>
    internal static bool TryGetDay(string rootDirectory, string artifactPath, out DateOnly day)
    {
        var root = NormalizeRoot(rootDirectory);
        day = default;
        if (string.IsNullOrWhiteSpace(artifactPath) || !Path.IsPathFullyQualified(artifactPath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(artifactPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(fullPath);
        var daySegment = directory is null ? null : Path.GetFileName(directory);
        if (daySegment is null
            || !DateOnly.TryParseExact(
                daySegment,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDay))
        {
            return false;
        }

        // Comparing the complete leaf validates the month, ISO week-year/week, day, and direct file placement.
        var expectedDirectory = GetDayDirectory(root, parsedDay);
        if (!string.Equals(directory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        day = parsedDay;
        return true;
    }

    /// <summary>Enumerates every WorkTrail-owned artifact below the configured screenshot root.</summary>
    internal static IEnumerable<string> EnumerateOwnedArtifacts(string rootDirectory)
    {
        var root = NormalizeRoot(rootDirectory);
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", RecursiveEnumeration))
        {
            if (ScreenCaptureService.IsOwnedArtifact(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>Enumerates WorkTrail-owned artifacts directly inside one validated layout directory.</summary>
    internal static IEnumerable<string> EnumerateOwnedArtifactsInDirectory(string directory)
    {
        var fullDirectory = NormalizeRoot(directory);
        if (!Directory.Exists(fullDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (ScreenCaptureService.IsOwnedArtifact(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>Returns whether a fully qualified path is the root itself or one of its descendants.</summary>
    internal static bool IsSameOrDescendant(string path, string rootDirectory)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = NormalizeRoot(rootDirectory);
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("A fully qualified screenshot directory is required.", nameof(rootDirectory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
    }

    // path must not traverse a junction or symbolic link when writing or deleting owned artifacts.
    internal static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Screenshot storage does not follow filesystem links.");
    }
}
