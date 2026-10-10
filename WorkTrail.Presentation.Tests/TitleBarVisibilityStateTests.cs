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


using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Verifies that automatic chrome remains safe for mouse, touch, and keyboard interaction.</summary>
public sealed class TitleBarVisibilityStateTests
{
    /// <summary>A contact during the hover reveal delay cannot activate a command that has not yet appeared.</summary>
    [Fact]
    public void FirstContactDuringDelayedRevealRemainsShieldedUntilRelease()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: true, pressed: false);
        Assert.True(state.IsVisible);
        Assert.True(state.BeginRevealContact(isChromeVisible: false));
        state.SetInteractionProtection(isProtected: true);
        state.UpdatePointer(inside: true, pressed: true);
        Assert.False(state.ShouldShowChrome(isChromeVisible: false));
        state.CompleteRevealContact();
        Assert.True(state.ShouldShowChrome(isChromeVisible: false));
    }

    /// <summary>A drag begun during the hide delay cancels hiding until release, even after hover intent has changed.</summary>
    [Fact]
    public void PressDuringDelayedHideProtectsRenderedChrome()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: false, pressed: false);
        Assert.False(state.IsVisible);
        state.UpdatePointer(inside: false, pressed: true);
        Assert.True(state.ShouldShowChrome(isChromeVisible: true));
        Assert.False(state.ShouldShowChrome(isChromeVisible: false));
        state.UpdatePointer(inside: false, pressed: false);
        Assert.False(state.ShouldShowChrome(isChromeVisible: true));
    }

    /// <summary>Mouse exit hides chrome, but an active window drag retains its controls until release.</summary>
    [Fact]
    public void HoverOutsideHidesAfterPointerDragEnds()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: true, pressed: false);
        state.UpdatePointer(inside: false, pressed: true);
        Assert.True(state.IsVisible);
        state.UpdatePointer(inside: false, pressed: false);
        Assert.False(state.IsVisible);
        state.UpdatePointer(inside: true, pressed: false);
        Assert.True(state.IsVisible);
    }

    /// <summary>The entire first touch is consumed before caption buttons become interactive.</summary>
    [Fact]
    public void FirstTouchRevealsOnlyAfterRelease()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: false, pressed: false);
        Assert.True(state.BeginRevealContact(isChromeVisible: false));
        state.UpdatePointer(inside: true, pressed: true);
        state.RevealForInteraction();
        Assert.False(state.IsVisible);
        Assert.True(state.IsRevealContactPending);
        state.CompleteRevealContact();
        Assert.True(state.IsVisible);
        Assert.False(state.IsRevealContactPending);
        Assert.False(state.BeginRevealContact(isChromeVisible: true));
    }

    /// <summary>Keyboard navigation remains usable outside mouse hover, then hands control back to the mouse.</summary>
    [Fact]
    public void KeyboardRevealSurvivesPointerPollingUntilMouseMoves()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: false, pressed: false);
        state.RevealForInteraction();
        state.UpdatePointer(inside: false, pressed: false);
        Assert.True(state.IsVisible);
        state.ResumeHover(inside: false, pressed: false);
        Assert.False(state.IsVisible);
    }

    /// <summary>A title-bar flyout or focused command remains visible when pointer events come from another HWND.</summary>
    [Fact]
    public void FocusedCommandOrPopupStaysVisibleUntilInteractionEnds()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: true, pressed: false);
        state.SetInteractionProtection(isProtected: true);
        state.ResumeHover(inside: false, pressed: false);
        Assert.True(state.IsVisible);
        state.UpdatePointer(inside: false, pressed: false);
        Assert.True(state.IsVisible);
        state.SetInteractionProtection(isProtected: false);
        Assert.False(state.IsVisible);
    }

    /// <summary>Ending popup protection during a drag cannot remove the native caption until the pointer releases.</summary>
    [Fact]
    public void PointerDragSurvivesProtectionEnding()
    {
        var state = new TitleBarVisibilityState();
        state.SetInteractionProtection(isProtected: true);
        state.UpdatePointer(inside: false, pressed: true);
        state.SetInteractionProtection(isProtected: false);
        Assert.True(state.IsVisible);
        state.UpdatePointer(inside: false, pressed: false);
        Assert.False(state.IsVisible);
    }

    /// <summary>Focus protection must not expose a command under the touch used to reveal the hidden title bar.</summary>
    [Fact]
    public void FocusProtectionDoesNotBypassTheFirstTouchShield()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: false, pressed: false);
        Assert.True(state.BeginRevealContact(isChromeVisible: false));
        state.SetInteractionProtection(isProtected: true);
        Assert.False(state.IsVisible);
        state.CompleteRevealContact();
        Assert.True(state.IsVisible);
    }

    /// <summary>Touch cancellation or window deactivation cannot leave the input shield waiting forever.</summary>
    [Fact]
    public void EndingInteractionCancelsPendingTouch()
    {
        var state = new TitleBarVisibilityState();
        state.UpdatePointer(inside: false, pressed: false);
        Assert.True(state.BeginRevealContact(isChromeVisible: false));
        state.EndInteraction();
        Assert.False(state.IsRevealContactPending);
        Assert.False(state.IsVisible);
        state.CompleteRevealContact();
        Assert.False(state.IsVisible);
    }
}
