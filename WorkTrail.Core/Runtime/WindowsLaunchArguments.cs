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
using System.Runtime.InteropServices;

namespace WorkTrail.Runtime;

/// <summary>Decodes Windows launch payloads without reading another process's command line.</summary>
public static class WindowsLaunchArguments
{
    /// <summary>Parses packaged argument payloads and unpackaged command lines using Windows quoting rules.</summary>
    /// <param name="arguments">The arguments supplied by the redirected launch activation.</param>
    /// <param name="executableFileName">The application executable name, without its directory.</param>
    /// <returns>The launch options supplied to the process that requested activation.</returns>
    public static LaunchOptions Parse(string arguments, string executableFileName)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableFileName);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            // An empty activation is a normal shell launch; do not let the OS substitute this process's original arguments.
            return LaunchOptions.Parse([]);
        }

        // The fixed leading token lets the OS apply argument quoting consistently to both supported payload shapes.
        var argumentVector = CommandLineToArgvW("activation " + arguments, out var argumentCount);
        if (argumentVector == IntPtr.Zero)
        {
            // Native parsing failure is terminal; dropping options could accidentally enable tracking.
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var values = new List<string>(argumentCount - 1);
            for (var index = 1; index < argumentCount; index++)
            {
                values.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argumentVector, index * IntPtr.Size))
                    ?? throw new InvalidOperationException("Windows returned an invalid launch argument."));
            }

            // App SDK launch payloads include the executable; packaged platform payloads contain only the arguments.
            if (values.Count > 0 && string.Equals(Path.GetFileName(values[0]), executableFileName, StringComparison.OrdinalIgnoreCase))
            {
                values.RemoveAt(0);
            }

            return LaunchOptions.Parse(values);
        }
        finally
        {
            _ = LocalFree(argumentVector);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
