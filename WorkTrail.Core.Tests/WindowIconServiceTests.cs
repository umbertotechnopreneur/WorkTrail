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
using System.Runtime.InteropServices;
using System.Text.Json;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WindowIconServiceTests
{
    /// <summary>A missing foreground window has no icon.</summary>
    [Fact]
    public void MissingWindowHasNoIcon() => Assert.Null(WindowIconService.ReadPixels(0));

    /// <summary>Reads a real USER32 window icon repeatedly without taking ownership of its source handle.</summary>
    [Fact]
    public void NativeWindowIconProducesPixelsAndPreservesBorrowedHandle()
    {
        // This test creates only an invisible native window; it does not change foreground focus.
        var window = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 1, 1, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            var icon = LoadIcon(0, 32512 /* IDI_APPLICATION, shared system resource */);
            Assert.NotEqual(0, icon);
            SendMessage(window, 0x0080 /* WM_SETICON */, 0 /* ICON_SMALL */, icon);

            var first = WindowIconService.ReadPixels(window);
            Assert.NotNull(first);
            Assert.Equal(16 * 16 * 4, first.Length);
            Assert.Contains(first, value => value != 0);
            Assert.Equal(first, WindowIconService.ReadPixels(window));
            Assert.Equal(icon, SendMessage(window, 0x007F /* WM_GETICON */, 0, 0));
        }
        finally
        {
            Assert.True(DestroyWindow(window));
        }
    }

    /// <summary>Live icon data crosses dashboard JSON but is excluded from activity serialization.</summary>
    [Fact]
    public void IconIsLiveDashboardDataOnly()
    {
        var pixels = new byte[16 * 16 * 4];
        pixels[3] = 255;
        var sample = new ActivitySample(DateTimeOffset.UtcNow, 5, "active", "test", "Test", "", "",
            "test-installation", 0, 0)
        {
            ApplicationIconPixels = pixels
        };
        Assert.DoesNotContain("ApplicationIconPixels", JsonSerializer.Serialize(sample));

        var dashboard = new DashboardState("RUNNING", "Test", 0, 0, 5, 0, true,
            sample.Timestamp, sample.Timestamp, sample.Timestamp)
        {
            CurrentApplicationIconPixels = pixels
        };
        var restored = JsonSerializer.Deserialize<DashboardState>(JsonSerializer.Serialize(dashboard));
        Assert.Equal(pixels, restored!.CurrentApplicationIconPixels);
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern nint LoadIcon(nint instance, nint name);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
