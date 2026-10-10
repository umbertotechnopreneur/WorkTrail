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


namespace WorkTrail.Presentation;

/// <summary>Tracks pointer and accessible keyboard/touch access to automatically hidden window chrome.</summary>
public sealed class TitleBarVisibilityState
{
    private bool _pointerInside;
    private bool _pointerPressed;
    private bool _interactionHeld;
    private bool _interactionProtected;

    /// <summary>Gets whether the window chrome should be visible and interactive.</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>Gets whether the first contact is being consumed to reveal hidden chrome.</summary>
    public bool IsRevealContactPending { get; private set; }

    /// <summary>Resolves hover intent without interrupting a drag begun while the rendered chrome is awaiting its hide delay.</summary>
    public bool ShouldShowChrome(bool isChromeVisible) =>
        !IsRevealContactPending && (IsVisible || (_pointerPressed && isChromeVisible));

    /// <summary>Updates hover without hiding chrome during an ongoing pointer drag or accessibility interaction.</summary>
    public void UpdatePointer(bool inside, bool pressed)
    {
        _pointerInside = inside;
        _pointerPressed = pressed;
        if (!IsRevealContactPending)
        {
            IsVisible = inside || (pressed && IsVisible) || _interactionHeld || _interactionProtected;
        }
    }

    /// <summary>Keeps focused title-bar commands and their open popups available even when mouse hover ends.</summary>
    public void SetInteractionProtection(bool isProtected)
    {
        _interactionProtected = isProtected;
        UpdatePointer(_pointerInside, _pointerPressed);
    }

    /// <summary>Returns to mouse hover behavior when the user moves a hovering pointer.</summary>
    public void ResumeHover(bool inside, bool pressed)
    {
        _interactionHeld = false;
        UpdatePointer(inside, pressed);
    }

    /// <summary>Consumes the first contact on hidden chrome until its release, so it cannot click a revealed command.</summary>
    /// <param name="isChromeVisible">The rendered visibility, which can lag hover intent during its delay.</param>
    public bool BeginRevealContact(bool isChromeVisible)
    {
        if (isChromeVisible || IsRevealContactPending)
        {
            return false;
        }

        IsRevealContactPending = true;
        IsVisible = false;
        return true;
    }

    /// <summary>Reveals chrome only after the revealing contact has ended.</summary>
    public void CompleteRevealContact()
    {
        if (!IsRevealContactPending)
        {
            return;
        }

        IsRevealContactPending = false;
        RevealForInteraction();
    }

    /// <summary>Keeps chrome available while the user navigates with keyboard or touch.</summary>
    public void RevealForInteraction()
    {
        if (!IsRevealContactPending)
        {
            _interactionHeld = true;
            IsVisible = true;
        }
    }

    /// <summary>Ends an accessibility interaction on deactivation or after its idle timeout.</summary>
    public void EndInteraction()
    {
        _interactionHeld = false;
        IsRevealContactPending = false;
        UpdatePointer(_pointerInside, _pointerPressed);
    }
}
