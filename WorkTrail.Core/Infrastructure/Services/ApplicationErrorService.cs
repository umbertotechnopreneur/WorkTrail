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


using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WorkTrail.Services;

/// <summary>Persists unexpected failures and shows a native dialog even before WinUI creates a window.</summary>
public static class ApplicationErrorService
{
    private static readonly object LogGate = new();
    private static ILogger? _logger;
    private static int _dialogActive;

    /// <summary>Connects the error reporter to the application's normal logging pipeline.</summary>
    /// <param name="logger">The initialized process logger.</param>
    /// <exception cref="ArgumentNullException">The logger is missing.</exception>
    public static void Configure(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Volatile.Write(ref _logger, logger);
    }

    /// <summary>Logs a managed exception and presents one error dialog without throwing another failure.</summary>
    /// <param name="exception">The unexpected failure, including its original stack and inner exceptions.</param>
    /// <param name="source">The trusted application boundary that caught the failure.</param>
    /// <param name="language">The app locale, or system to use the Windows UI locale.</param>
    /// <param name="deferDialog">Moves acknowledgement off a finalizer thread after the log has been saved.</param>
    public static void Report(Exception exception, string source, string language = "system", bool deferDialog = false)
    {
        try
        {
            Volatile.Read(ref _logger)?.LogCritical(exception, "Unexpected application failure. Source={Source}", source);
        }
        catch (Exception loggingFailure)
        {
            Debug.WriteLine($"WorkTrail logging failed: {loggingFailure.GetType().Name}");
        }

        // This independent, synchronously flushed log also works before logging initialization or process termination.
        var logPath = TryWriteExceptionLog(exception, source);
        if (!OperatingSystem.IsWindows() || Interlocked.CompareExchange(ref _dialogActive, 1, 0) != 0)
        {
            return;
        }

        if (deferDialog)
        {
            try
            {
                // Unobserved task notifications run on the finalizer thread; user acknowledgement must not block GC.
                if (ThreadPool.QueueUserWorkItem(_ => ShowErrorDialog(exception, logPath, language)))
                {
                    return;
                }
            }
            catch (Exception schedulingFailure)
            {
                _ = TryWriteExceptionLog(schedulingFailure, "ErrorDialogScheduling");
            }
        }
        ShowErrorDialog(exception, logPath, language);
    }

    /// <summary>Shows one acknowledgement dialog and releases the modal guard even if native UI fails.</summary>
    /// <param name="exception">The failure already saved to the diagnostic log.</param>
    /// <param name="logPath">The saved log path, or null when writing failed.</param>
    /// <param name="language">The locale used for the user-facing error.</param>
    private static void ShowErrorDialog(Exception exception, string? logPath, string language)
    {
        try
        {
            var title = "WorkTrail error";
            var detail = exception.GetBaseException().Message;
            if (detail.Length > 2000)
            {
                detail = detail[..2000] + "…";
            }
            var message = $"An unexpected error occurred.\n\n{detail}\n\nDiagnostic log: {logPath ?? "The diagnostic log could not be saved."}";
            try
            {
                var strings = new LocalizationService(language);
                title = strings.Translate("Error.Unhandled.Title");
                message = strings.Format("Error.Unhandled.Message", detail,
                    logPath ?? strings.Translate("Error.Unhandled.LogUnavailable"));
            }
            catch (Exception localizationFailure)
            {
                // Broken or unavailable resources must not hide the original startup error.
                Debug.WriteLine($"WorkTrail error localization failed: {localizationFailure.GetType().Name}");
            }

            // A native dialog needs neither a database nor a WinUI owner/XamlRoot, and works on fatal worker threads.
            const uint errorIcon = 0x00000010;
            const uint taskModal = 0x00002000;
            const uint foreground = 0x00010000;
            const uint errorDialogFlags = errorIcon | taskModal | foreground;
            if (MessageBox(IntPtr.Zero, message, title, errorDialogFlags) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception dialogFailure)
        {
            _ = TryWriteExceptionLog(dialogFailure, "ErrorDialog");
        }
        finally
        {
            // Concurrent failures are logged but cannot create an unbounded cascade of modal dialogs.
            Volatile.Write(ref _dialogActive, 0);
        }
    }

    /// <summary>Writes and flushes an emergency log independently of application services and SQLite.</summary>
    /// <param name="exception">The exception to preserve.</param>
    /// <param name="source">The trusted failure boundary written with the exception.</param>
    private static string? TryWriteExceptionLog(Exception exception, string source)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkTrail", "logs");
            var logPath = Path.Combine(directory, "worktrail-unhandled.log");
            lock (LogGate)
            {
                Directory.CreateDirectory(directory);
                using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.WriteLine($"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} Source={source}");
                writer.WriteLine(exception);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            return logPath;
        }
        catch (Exception loggingFailure)
        {
            Debug.WriteLine($"WorkTrail emergency logging failed: {loggingFailure.GetType().Name}");
            return null;
        }
    }

    /// <summary>Shows the Windows error acknowledgement dialog without depending on WinUI initialization.</summary>
    /// <param name="owner">The optional native owner window; zero supports early startup.</param>
    /// <param name="text">The localized error summary and diagnostic log location.</param>
    /// <param name="caption">The localized dialog title.</param>
    /// <param name="flags">The native icon, modality and foreground flags.</param>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
}
