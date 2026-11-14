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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace WorkTrail.Controls;

/// <summary>Provides a horizontal drag thumb with the native east-west resize cursor.</summary>
public sealed class HorizontalResizeGrip : ContentControl
{
    private readonly Thumb _thumb = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsTabStop = false,
        AllowFocusOnInteraction = false,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
    };

    /// <summary>Forwards the native thumb's horizontal drag movement.</summary>
    public event DragDeltaEventHandler DragDelta
    {
        add => _thumb.DragDelta += value;
        remove => _thumb.DragDelta -= value;
    }

    /// <summary>Forwards completion or cancellation of the native drag interaction.</summary>
    public event DragCompletedEventHandler DragCompleted
    {
        add => _thumb.DragCompleted += value;
        remove => _thumb.DragCompleted -= value;
    }

    /// <summary>Creates the resize grip with a native horizontal-resize pointer.</summary>
    public HorizontalResizeGrip()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = _thumb;
    }
}
