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


using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Finds, opens, bounds, and redacts the existing rolling application log.</summary>
public sealed partial class ApplicationLogService
{
    private const long MaximumSharedSourceBytes = 1_048_576;
    private const int MaximumRetainedExports = 4;
    private static readonly TimeSpan MaximumExportAge = TimeSpan.FromDays(1);

    private readonly string? _logDirectory;
    private readonly string _exportDirectory;
    private readonly WindowsFileShareService _fileShare;

    /// <summary>Creates a support-log service over the process-wide Serilog directory.</summary>
    public ApplicationLogService(
        string? logDirectory = null,
        string? exportDirectory = null,
        WindowsFileShareService? fileShare = null)
    {
        _logDirectory = logDirectory ?? new ObservabilityConfigurationService().Load().LogDirectory;
        _exportDirectory = exportDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkTrail",
            "support-logs");
        _fileShare = fileShare ?? new WindowsFileShareService();
    }

    /// <summary>Opens the newest rolling log with the Windows shell.</summary>
    public string OpenLatestLog()
    {
        var path = FindLatestLogPath();
        // Shell invocation remains inside infrastructure; the view receives only the operation result.
        _ = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })
            ?? throw new InvalidOperationException("Windows did not open the application log.");
        return path;
    }

    /// <summary>Opens the directory that contains the rolling application logs.</summary>
    public string OpenLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory))
        {
            throw new DirectoryNotFoundException("The application log directory is unavailable.");
        }

        // Shell invocation remains inside infrastructure; the application facade exposes only the operation result.
        _ = Process.Start(new ProcessStartInfo { FileName = _logDirectory, UseShellExecute = true })
            ?? throw new InvalidOperationException("Windows did not open the application log directory.");
        return _logDirectory;
    }

    /// <summary>Creates a bounded redacted copy and opens the Windows Share UI for that copy only.</summary>
    public string ShareLatestRedactedLog(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid owner window handle is required.", nameof(windowHandle));
        }

        var exportPath = CreateRedactedExport();
        return _fileShare.Share(
            exportPath,
            windowHandle,
            "WorkTrail support log",
            "Redacted WorkTrail diagnostics");
    }

    /// <summary>Creates one bounded, redacted support-log export without opening external UI.</summary>
    internal string CreateRedactedExport()
    {
        var sourcePath = FindLatestLogPath();
        var sourceTail = ReadBoundedTail(sourcePath);
        var redacted = RedactForSharing(sourceTail);

        Directory.CreateDirectory(_exportDirectory);
        var exportPath = Path.Combine(
            _exportDirectory,
            $"worktrail-support-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.log");
        var header = $"WorkTrail support log (redacted){Environment.NewLine}Generated UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}---{Environment.NewLine}";
        File.WriteAllText(exportPath, header + redacted, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        PruneExports();
        return exportPath;
    }

    /// <summary>Returns the newest existing rolling log path.</summary>
    internal string FindLatestLogPath()
    {
        if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory))
        {
            throw new DirectoryNotFoundException("The application log directory is unavailable.");
        }

        return Directory.EnumerateFiles(_logDirectory, "worktrail-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.FullName)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("No application log is available yet.");
    }

    /// <summary>Removes secrets, private paths, and raw identifiers from support-log text.</summary>
    internal static string RedactForSharing(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var redacted = WindowsPathPattern().Replace(value, "[path]");
        redacted = UncPathPattern().Replace(redacted, "[path]");
        redacted = AuthorizationPattern().Replace(redacted, "[secret]");
        redacted = SecretAssignmentPattern().Replace(redacted, "[secret]");
        redacted = OpenAiKeyPattern().Replace(redacted, "[secret]");
        redacted = GuidPattern().Replace(redacted, "[id]");
        return redacted;
    }

    private static string ReadBoundedTail(string sourcePath)
    {
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - MaximumSharedSourceBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: start == 0, leaveOpen: false);
        if (start > 0)
        {
            _ = reader.ReadLine();
        }

        return reader.ReadToEnd();
    }

    private void PruneExports()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var exports = Directory.EnumerateFiles(_exportDirectory, "worktrail-support-*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            foreach (var export in exports.Where((file, index) =>
                         index >= MaximumRetainedExports ||
                         now.UtcDateTime - file.LastWriteTimeUtc > MaximumExportAge))
            {
                export.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Export cleanup is best effort; failure must not prevent creation of the requested redacted copy.
        }
    }

    [GeneratedRegex(@"(?im)\b[a-z]:\\[^\r\n]*")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?im)\\\\[^\r\n]*")]
    private static partial Regex UncPathPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex("""(?i)"?\b(?:api[_-]?key|token|secret|authorization|dsn)\b"?\s*[:=]\s*(?:"(?:\\.|[^"\r\n])*"|[^\s,}\r\n]+)""")]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(@"(?i)\bsk-[A-Za-z0-9_-]{8,}")]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex(@"(?i)\b(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\b")]
    private static partial Regex GuidPattern();
}
