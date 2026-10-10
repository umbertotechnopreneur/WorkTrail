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


using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace WorkTrail;

/// <summary>Applies the Z translation required for a global ThemeShadow on InfoBars.</summary>
public static class InfoBarElevationBehavior
{
    private const float Elevation = 12f;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(InfoBarElevationBehavior),
        new PropertyMetadata(false, IsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void IsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not InfoBar infoBar)
        {
            throw new InvalidOperationException("InfoBar elevation can only be attached to an InfoBar.");
        }

        var isEnabled = (bool)args.NewValue;
        ElementCompositionPreview.SetIsTranslationEnabled(infoBar, isEnabled);
        infoBar.Translation = isEnabled ? new Vector3(0f, 0f, Elevation) : Vector3.Zero;
    }
}
