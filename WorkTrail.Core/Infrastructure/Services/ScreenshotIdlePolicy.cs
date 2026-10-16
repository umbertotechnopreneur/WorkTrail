// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WorkTrail.Providers;

namespace WorkTrail.Services;

/// <summary>Gates scheduled screenshots without changing recorded user-activity state.</summary>
internal static class ScreenshotIdlePolicy
{
    internal static bool CanCaptureNow(ScreenshotCaptureRules policy, AppSettings? settings = null)
    {
        try
        {
            // Read Windows directly: a cached activity sample can refer to a different foreground app.
            var window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero
                || NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0
                || processId == 0)
                throw new InvalidOperationException("The foreground window is unavailable for the scheduled capture policy.");

            using var process = Process.GetProcessById(checked((int)processId));
            var processName = process.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
                throw new InvalidOperationException("The foreground process is unavailable for the scheduled capture policy.");

            var includeTitle = settings is null || ActivityContextProviderRegistry.IsDetailEnabled(processName, settings);
            var title = includeTitle ? ReadWindowTitle(window) : string.Empty;
            var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };
            if (!NativeMethods.GetLastInputInfo(ref info))
                throw new InvalidOperationException("Windows user-input timing is unavailable for the scheduled capture policy.");

            var currentTickCount = unchecked((uint)Environment.TickCount);
            if (NativeMethods.GetForegroundWindow() != window
                || NativeMethods.GetWindowThreadProcessId(window, out var currentProcessId) == 0
                || currentProcessId != processId
                || (includeTitle && !string.Equals(title, ReadWindowTitle(window), StringComparison.Ordinal)))
                throw new ScreenshotSceneChangedException("The foreground context changed while evaluating the scheduled capture policy.");

            return IsWithinIdleTimeout(currentTickCount, info.Time, policy.GetIdleTimeoutSeconds(processName, title));
        }
        catch (Exception exception) when (exception is not ScreenshotSceneChangedException
            && exception is (InvalidOperationException or ArgumentException
                or Win32Exception or OverflowException or DllNotFoundException or EntryPointNotFoundException))
        {
            // The application logs this skipped attempt; it must not become a repeated capture-failure notification.
            throw new ScreenshotIdlePolicyException(exception);
        }
    }

    internal static bool IsWithinIdleTimeout(uint currentTickCount, uint lastInputTickCount, int timeoutSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSeconds);
        // Unsigned subtraction preserves the Win32 input clock across its 32-bit rollover.
        var elapsedMilliseconds = unchecked(currentTickCount - lastInputTickCount);
        return elapsedMilliseconds < (ulong)timeoutSeconds * 1000;
    }

    private static string ReadWindowTitle(IntPtr window)
    {
        var title = new StringBuilder(512);
        return NativeMethods.GetWindowText(window, title, title.Capacity) > 0 ? title.ToString().Trim() : string.Empty;
    }
}

/// <summary>Signals an unavailable idle-policy probe, not a failed screenshot acquisition.</summary>
internal sealed class ScreenshotIdlePolicyException(Exception innerException)
    : InvalidOperationException("The scheduled capture idle policy could not be evaluated safely.", innerException)
{
}
