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


using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace WorkTrail.Controls;

/// <summary>Provides a horizontal drag thumb with the native east-west resize cursor.</summary>
public sealed class HorizontalResizeGrip : Control
{
    /// <summary>Creates the resize grip with a native horizontal-resize pointer.</summary>
    public HorizontalResizeGrip() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
