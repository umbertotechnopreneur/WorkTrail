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


using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using Windows.UI;

namespace WorkTrail.Controls;

/// <summary>Renders label DTOs with a shared icon vocabulary and palette.</summary>
internal static class ActivityLabelVisuals
{
    internal static string Glyph(string icon) => icon switch
    {
        "folder" => "\uE8B7",
        "work" => "\uE821",
        "study" => "\uE7BE",
        "home" => "\uE80F",
        "code" => "\uE943",
        "book" => "\uE82D",
        "edit" => "\uE70F",
        "music" => "\uE8D6",
        "art" => "\uE790",
        "health" => "\uE95E",
        "sport" => "\uEADA",
        "travel" => "\uE709",
        "globe" => "\uE774",
        "tools" => "\uE90F",
        "heart" => "\uEB51",
        "gift" => "\uE734",
        _ => throw new ArgumentException("Unsupported label icon.", nameof(icon))
    };

    internal static SolidColorBrush Brush(string hex) => new(Color.FromArgb(255,
        byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber),
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber),
        byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber)));

    internal static FontIcon Icon(string icon, string color) => new()
    {
        Glyph = Glyph(icon),
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 16,
        Foreground = Brush(color)
    };

    internal static FrameworkElement Content(ActivityLabelDefinition label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        panel.Children.Add(Icon(label.Icon, label.Color));
        panel.Children.Add(new TextBlock
        {
            Text = label.Name,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 135,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        AutomationProperties.SetName(panel, label.Name);
        ToolTipService.SetToolTip(panel, label.Name);
        return panel;
    }
}
