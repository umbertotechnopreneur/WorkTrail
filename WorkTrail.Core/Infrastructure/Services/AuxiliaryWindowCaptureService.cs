// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkTrail.Services;

/// <summary>Excludes local auxiliary windows without changing their visibility, placement or focus.</summary>
internal static class AuxiliaryWindowCaptureService
{
    private const string ExcludedProperty = "WorkTrail.AuxiliaryCaptureExclusion.v1";
    private const uint ExcludeFromCapture = 0x11;

    internal static void Exclude(long windowHandle)
    {
        var window = new IntPtr(windowHandle);
        // Display affinity must be set in the UI process, even when the shared runtime lives elsewhere.
        if (GetWindowThreadProcessId(window, out var process) == 0 || process != Environment.ProcessId)
            throw new ArgumentException("Capture exclusion requires a window owned by this UI process.", nameof(windowHandle));
        if (!SetWindowDisplayAffinity(window, ExcludeFromCapture))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to exclude the auxiliary window from capture.");
        // The HWND property lets the same-user runtime identify excluded peers without process-name guesses.
        if (!SetProp(window, ExcludedProperty, new IntPtr(1)))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to mark the auxiliary capture exclusion.");
    }

    internal static bool IsExcluded(IntPtr window) => GetProp(window, ExcludedProperty) == new IntPtr(1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", EntryPoint = "SetPropW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProp(IntPtr window, string name, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);
}
