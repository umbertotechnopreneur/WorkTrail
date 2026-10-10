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
using System.Collections.Generic;
using WorkTrail.Application;
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class WorkspaceWindowStateTests
{
    /// <summary>Verifies that the first launch does not invent auxiliary windows without a saved session.</summary>
    [Fact]
    public void MissingSession_DoesNotOpenAuxiliaryWindows()
    {
        Assert.Empty(WorkspaceWindowState.GetWindowsToRestore(null));
    }

    /// <summary>Verifies that temporary owner hiding cannot silently close saved auxiliary work surfaces.</summary>
    [Theory]
    [InlineData(WindowStateKeys.Main, false, false)]
    [InlineData(WindowStateKeys.Main, true, true)]
    [InlineData(WindowStateKeys.About, false, true)]
    [InlineData(WindowStateKeys.WorldMap, false, true)]
    [InlineData(WindowStateKeys.LunarPhase, true, true)]
    [InlineData(WindowStateKeys.LocalSky, false, true)]
    [InlineData(WindowStateKeys.AstronomyAgenda, false, true)]
    [InlineData(WindowStateKeys.CelestialMap, false, true)]
    public void LiveWindowState_DistinguishesMainTrayHidingFromAnOpenAuxiliary(string key, bool isVisible, bool expected)
    {
        Assert.Equal(expected, WorkspaceWindowState.IsOpenWhileAlive(key, isVisible));
    }

    /// <summary>Verifies that explicitly closed work surfaces remain closed and open surfaces restore deterministically.</summary>
    [Fact]
    public void Restore_PreservesOpenAndClosedWorkSurfaces()
    {
        var openStates = new Dictionary<string, bool>
        {
            [WindowStateKeys.LunarPhase] = true,
            [WindowStateKeys.Main] = false,
            [WindowStateKeys.WorldClocks] = false,
            [WindowStateKeys.WorldMap] = true,
            [WindowStateKeys.Search] = false
        };

        Assert.Equal(
            new[] { WindowStateKeys.WorldMap, WindowStateKeys.LunarPhase },
            WorkspaceWindowState.GetWindowsToRestore(openStates));
    }

    /// <summary>New celestial windows restore independently of the clocks and original day/night map.</summary>
    [Fact]
    public void Restore_CelestialSurfacesDoNotOpenTheOriginalMapOrClocks()
    {
        var states = new Dictionary<string, bool>
        {
            [WindowStateKeys.CelestialMap] = true,
            [WindowStateKeys.LocalSky] = true,
            [WindowStateKeys.AstronomyAgenda] = true,
            [WindowStateKeys.WorldMap] = false,
            [WindowStateKeys.WorldClocks] = false
        };
        Assert.Equal(new[] { WindowStateKeys.LocalSky, WindowStateKeys.AstronomyAgenda, WindowStateKeys.CelestialMap },
            WorkspaceWindowState.GetWindowsToRestore(states));
        Assert.True(WindowStateService.GetMinimumSize(WindowStateKeys.LocalSky).Width > 0);
    }

    /// <summary>Verifies that retained child windows restore only after the owner needed to display them.</summary>
    [Fact]
    public void Restore_OpenChildrenAddTheirOwnersInOrder()
    {
        var openStates = new Dictionary<string, bool>
        {
            [WindowStateKeys.OcrText] = true,
            [WindowStateKeys.Screenshots] = false,
            [WindowStateKeys.Licenses] = true,
            [WindowStateKeys.About] = false
        };

        Assert.Equal(
            new[] { WindowStateKeys.Screenshots, WindowStateKeys.OcrText, WindowStateKeys.About, WindowStateKeys.Licenses },
            WorkspaceWindowState.GetWindowsToRestore(openStates));
    }

    /// <summary>Verifies that a closed child does not reopen its previously closed owner.</summary>
    [Fact]
    public void Restore_ClosedChildrenDoNotCreateOwnerWindows()
    {
        var openStates = new Dictionary<string, bool>
        {
            [WindowStateKeys.OcrText] = false,
            [WindowStateKeys.Licenses] = false
        };

        Assert.Empty(WorkspaceWindowState.GetWindowsToRestore(openStates));
    }

    /// <summary>Verifies that startup cannot replay an operation or configuration dialog from a saved visibility flag.</summary>
    [Theory]
    [InlineData(WindowStateKeys.QuickSetup)]
    [InlineData(WindowStateKeys.Dialog)]
    [InlineData(WindowStateKeys.ActivityCalendar)]
    [InlineData(WindowStateKeys.SearchIndexing)]
    [InlineData(WindowStateKeys.WorldClockCityPicker)]
    [InlineData(WindowStateKeys.AiScreenshotReprocessing)]
    [InlineData(WindowStateKeys.AiPricing)]
    [InlineData(WindowStateKeys.AiConnectionTest)]
    public void Restore_LeavesTransientSurfacesClosed(string windowKey)
    {
        Assert.False(WorkspaceWindowState.IsRestorable(windowKey));
        Assert.Empty(WorkspaceWindowState.GetWindowsToRestore(new Dictionary<string, bool> { [windowKey] = true }));
    }

    /// <summary>Verifies unsupported keys fail even when a malformed session marks them closed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Restore_RejectsUnknownWindowIdentities(bool isOpen)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceWindowState.IsRestorable("unknown-window"));
        Assert.Throws<ArgumentException>(() => WorkspaceWindowState.GetWindowsToRestore(
            new Dictionary<string, bool> { ["unknown-window"] = isOpen }));
    }
}
