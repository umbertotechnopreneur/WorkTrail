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
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class TrayIconServiceTests
{
    /// <summary>Registration exposes the tray icon without changing an already visible player's state.</summary>
    [Fact]
    public void ShowInNotificationArea_RegistersIconWithoutHidingPlayer()
    {
        using var fixture = new TrayFixture();
        _ = ShowWindow(fixture.Window, 4 /* SW_SHOWNOACTIVATE */);

        fixture.ShowInNotificationArea();

        Assert.True(fixture.Shell.IconPresent);
        Assert.True(IsWindowVisible(fixture.Window));
    }

    /// <summary>An explicit UI activation restores a player that was previously hidden in the notification area.</summary>
    [Fact]
    public void ShowMainWindow_RestoresHiddenPlayer()
    {
        using var fixture = new TrayFixture();
        fixture.Hide();

        fixture.Service.ShowMainWindow();

        Assert.True(IsWindowVisible(fixture.Window));
        Assert.True(fixture.Shell.IconPresent);
    }

    /// <summary>A taskbar rebuild restores the icon without changing the player's current visibility or duplicating owners.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarRecreation_RestoresLostIconAndPreservesWindowVisibility(bool visible)
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        if (visible)
        {
            _ = ShowWindow(fixture.Window, 4 /* SW_SHOWNOACTIVATE */);
        }

        fixture.Shell.IconPresent = false;
        fixture.NotifyTaskbarCreated();
        fixture.NotifyTaskbarCreated();

        Assert.True(fixture.Shell.IconPresent);
        Assert.Equal(2, fixture.Shell.AddCount);
        Assert.Equal(2, fixture.Shell.VersionCount);
        Assert.Equal("WorkTrail recovery test", fixture.Shell.ToolTip);
        Assert.Equal(visible, IsWindowVisible(fixture.Window));

        // A second Explorer restart must reuse the same callback registration and remain recoverable.
        fixture.Shell.IconPresent = false;
        fixture.NotifyTaskbarCreated();
        Assert.True(fixture.Shell.IconPresent);
        Assert.Equal(3, fixture.Shell.AddCount);
        Assert.Equal(visible, IsWindowVisible(fixture.Window));
    }

    /// <summary>Hiding verifies the live shell even if its recreation broadcast was missed.</summary>
    [Fact]
    public void Hide_RepairsMissingIconWithoutTaskbarBroadcast()
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        fixture.Shell.IconPresent = false;
        _ = ShowWindow(fixture.Window, 4);

        fixture.Hide();

        Assert.True(fixture.Shell.IconPresent);
        Assert.Equal(2, fixture.Shell.AddCount);
        Assert.False(IsWindowVisible(fixture.Window));
    }

    /// <summary>Both add and activation-configuration failures expose the player and allow recovery on a later broadcast.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(4u)]
    public void RecoveryFailure_RestoresPlayerAndAllowsRetry(uint failingMessage)
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        fixture.Shell.IconPresent = false;
        fixture.Shell.FailingMessage = failingMessage;

        fixture.NotifyTaskbarCreated();

        Assert.True(IsWindowVisible(fixture.Window));
        Assert.False(fixture.Shell.IconPresent);

        fixture.Shell.FailingMessage = null;
        fixture.NotifyTaskbarCreated();
        Assert.True(fixture.Shell.IconPresent);
        Assert.True(IsWindowVisible(fixture.Window));
        fixture.Hide();
        Assert.False(IsWindowVisible(fixture.Window));
    }

    /// <summary>A failed explicit hide reports the error and restores even an already-hidden player.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HideFailure_KeepsPlayerAvailableAndAllowsRetry(bool initiallyHidden)
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        if (!initiallyHidden)
        {
            _ = ShowWindow(fixture.Window, 4);
        }

        fixture.Shell.IconPresent = false;
        fixture.Shell.FailingMessage = 0;

        Assert.Throws<Win32Exception>(() => fixture.Hide());
        Assert.True(IsWindowVisible(fixture.Window));
        Assert.False(fixture.Shell.IconPresent);

        fixture.Shell.FailingMessage = null;
        fixture.Hide();
        Assert.True(fixture.Shell.IconPresent);
        Assert.False(IsWindowVisible(fixture.Window));
    }

    /// <summary>Initial configuration failure releases native ownership so the same service can attach successfully later.</summary>
    [Fact]
    public void InitialFailure_CleansPartialIconAndAllowsFreshAttach()
    {
        using var fixture = new TrayFixture();
        _ = ShowWindow(fixture.Window, 4);
        fixture.Shell.FailingMessage = 4;

        Assert.Throws<Win32Exception>(() => fixture.Hide());
        Assert.False(fixture.Shell.IconPresent);
        Assert.True(IsWindowVisible(fixture.Window));

        fixture.Shell.FailingMessage = null;
        fixture.Hide();
        Assert.True(fixture.Shell.IconPresent);
        Assert.False(IsWindowVisible(fixture.Window));
    }

    /// <summary>Shutdown removes the recovered icon and unsubclasses the window before later shell notifications.</summary>
    [Fact]
    public void Dispose_AfterRecoveryRemovesIconAndStopsCallbacks()
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        fixture.Shell.IconPresent = false;
        fixture.NotifyTaskbarCreated();

        fixture.Service.Dispose();
        fixture.NotifyTaskbarCreated();

        Assert.False(fixture.Shell.IconPresent);
        Assert.Equal(2, fixture.Shell.AddCount);
        Assert.Throws<ObjectDisposedException>(() => fixture.Hide());
    }

    /// <summary>Version-4 selection toggles the recovered player once, for either mouse or keyboard activation.</summary>
    [Theory]
    [InlineData(0x0400u)]
    [InlineData(0x0401u)]
    public void RecoveredIcon_UsesVersion4ActivationWithoutDoubleToggle(uint activation)
    {
        using var fixture = new TrayFixture();
        fixture.Hide();
        fixture.Shell.IconPresent = false;
        fixture.NotifyTaskbarCreated();
        _ = ShowWindow(fixture.Window, 4 /* SW_SHOWNOACTIVATE */);

        _ = SendMessage(fixture.Window, 0x8350, 0, (IntPtr)(0x10000 | activation));
        _ = SendMessage(fixture.Window, 0x8350, 0, (IntPtr)0x10202 /* raw WM_LBUTTONUP */);

        Assert.False(IsWindowVisible(fixture.Window));
        Assert.True(fixture.Shell.IconPresent);
        Assert.NotEqual(0u, fixture.Shell.Flags & 0x80 /* NIF_SHOWTIP */);
    }

    private sealed class TrayFixture : IDisposable
    {
        private readonly string _iconPath = Path.Combine(Path.GetTempPath(), $"WorkTrail-tray-test-{Guid.NewGuid():N}.ico");

        /// <summary>Creates an isolated native window and temporary icon without contacting Explorer.</summary>
        public TrayFixture()
        {
            using (var stream = File.Create(_iconPath))
            {
                SystemIcons.Application.Save(stream);
            }

            // Real USER32 subclass dispatch stays on this thread. An off-screen, nonactivating tool window cannot steal focus.
            Window = CreateWindowEx(0x08000080, "STATIC", "WorkTrail tray recovery test", 0x80000000,
                -32000, -32000, 1, 1, 0, 0, 0, 0);
            Assert.NotEqual(IntPtr.Zero, Window);
            Service = new TrayIconService(NullLogger.Instance, Shell.Notify);
        }

        public IntPtr Window { get; }
        public FakeShell Shell { get; } = new();
        public TrayIconService Service { get; }

        /// <summary>Exercises the public minimize command with complete localized labels.</summary>
        public void Hide() => Service.HideToNotificationArea(Window, _iconPath, "WorkTrail recovery test",
            new TrayIconMenuLabels("Show", "Hide", "Exit"));

        /// <summary>Exercises icon registration without changing the current window visibility.</summary>
        public void ShowInNotificationArea() => Service.ShowInNotificationArea(Window, _iconPath, "WorkTrail recovery test",
            new TrayIconMenuLabels("Show", "Hide", "Exit"));

        /// <summary>Dispatches the real registered Windows message only to this test window.</summary>
        public void NotifyTaskbarCreated()
        {
            var message = RegisterWindowMessage("TaskbarCreated");
            Assert.NotEqual(0u, message);
            _ = SendMessage(Window, message, 0, 0);
        }

        /// <summary>Releases the subclass, test HWND, and temporary icon in ownership order.</summary>
        public void Dispose()
        {
            Service.Dispose();
            Assert.True(DestroyWindow(Window));
            File.Delete(_iconPath);
        }
    }

    private sealed class FakeShell
    {
        public bool IconPresent { get; set; }
        public uint? FailingMessage { get; set; }
        public int AddCount { get; private set; }
        public int VersionCount { get; private set; }
        public string? ToolTip { get; private set; }
        public uint Flags { get; private set; }

        /// <summary>Models icon loss and shell failures while keeping native window dispatch real.</summary>
        public bool Notify(uint message, ref TrayIconService.NotifyIconData notification)
        {
            // Only shell registration is simulated: tests exercise real icon loading, HWND visibility, and subclass callbacks.
            if (message == FailingMessage)
            {
                Marshal.SetLastPInvokeError(5);
                return false;
            }

            switch (message)
            {
                case 0: // NIM_ADD
                    if (IconPresent) return false;
                    IconPresent = true;
                    AddCount++;
                    ToolTip = notification.ToolTip;
                    Flags = notification.Flags;
                    return true;
                case 1: // NIM_MODIFY
                    if (IconPresent) ToolTip = notification.ToolTip;
                    return IconPresent;
                case 2: // NIM_DELETE
                    IconPresent = false;
                    return true;
                case 4: // NIM_SETVERSION
                    if (!IconPresent || notification.Version != 4) return false;
                    VersionCount++;
                    return true;
                default:
                    throw new InvalidOperationException($"Unexpected shell operation {message}.");
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
