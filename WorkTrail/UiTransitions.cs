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


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace WorkTrail;

/// <summary>Shares presentation-only transitions between application surfaces.</summary>
internal static class UiTransitions
{
    /// <summary>Fades a newly shown panel into view without changing its geometry.</summary>
    public static void FadeIn(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Opacity = 0d;
        var animation = new DoubleAnimation
        {
            From = 0d,
            To = 1d,
            Duration = new Duration(TimeSpan.FromMilliseconds(180))
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
