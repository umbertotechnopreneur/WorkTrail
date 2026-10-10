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
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkTrail.Services;

/// <summary>Reads optional window icons through USER32, without file or network lookups.</summary>
internal static class WindowIconService
{
    internal const int IconSize = 16;

    /// <summary>Returns 16×16 premultiplied BGRA pixels, or null when Windows cannot supply an icon.</summary>
    internal static byte[]? ReadPixels(nint window)
    {
        if (window == 0)
        {
            // No foreground window means there is no optional decoration to show.
            return null;
        }

        try
        {
            // Bound cross-process calls: a hung/protected window must not stall activity sampling.
            nint borrowedIcon = 0;
            foreach (var iconKind in new nuint[] { 2 /* ICON_SMALL2 */, 0 /* ICON_SMALL */, 1 /* ICON_BIG */ })
            {
                if (SendMessageTimeout(window, 0x007F /* WM_GETICON */, iconKind,
                        96, 0x0002 /* SMTO_ABORTIFHUNG */, 100, out borrowedIcon) == 0)
                {
                    Debug.WriteLine("Foreground window icon unavailable: WM_GETICON failed or timed out.");
                    return null;
                }

                if (borrowedIcon != 0)
                {
                    break;
                }
            }

            if (borrowedIcon == 0)
            {
                // Windows documents class icons as the next source when WM_GETICON returns zero.
                borrowedIcon = ReadClassIcon(window, -34 /* GCLP_HICONSM */);
                if (borrowedIcon == 0)
                {
                    borrowedIcon = ReadClassIcon(window, -14 /* GCLP_HICON */);
                }
            }

            if (borrowedIcon == 0)
            {
                // An icon is optional; do not substitute an invented or generic application image.
                return null;
            }

            var ownedIcon = CopyIcon(borrowedIcon);
            if (ownedIcon == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                using var icon = Icon.FromHandle(ownedIcon);
                using var source = icon.ToBitmap();
                using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImage(source, new Rectangle(0, 0, IconSize, IconSize));
                }

                var pixels = new byte[IconSize * IconSize * 4];
                var data = bitmap.LockBits(new Rectangle(0, 0, IconSize, IconSize),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (var row = 0; row < IconSize; row++)
                    {
                        Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * IconSize * 4, IconSize * 4);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return pixels;
            }
            finally
            {
                // Only our copy is owned here; the window/class HICON must never be destroyed.
                if (!DestroyIcon(ownedIcon))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or ExternalException or ArgumentException)
        {
            // A closing window can invalidate its icon between calls. Report the failure and omit
            // this optional image; tracking still records the independently captured activity.
            Debug.WriteLine($"Foreground window icon unavailable: {exception.Message}");
            return null;
        }
    }

    private static nint ReadClassIcon(nint window, int index) => IntPtr.Size == 8
        ? GetClassLongPtr(window, index)
        : (nint)GetClassLong(window, index);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    private static extern nint GetClassLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW", SetLastError = true)]
    private static extern uint GetClassLong(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
