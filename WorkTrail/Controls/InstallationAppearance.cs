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
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;

namespace WorkTrail.Controls;

/// <summary>Maps the shared installation appearance contract to WinUI presentation values.</summary>
internal static class InstallationAppearance
{
    /// <summary>Creates an opaque WinUI brush for a supported installation accent.</summary>
    internal static SolidColorBrush CreateAccentBrush(string color)
    {
        if (!InstallationProfileCatalog.Colors.Contains(color, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported installation color '{color}'.");
        }

        var red = byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, red, green, blue));
    }

    /// <summary>Gets the Segoe Fluent glyph for a supported installation icon identifier.</summary>
    internal static string GetIconGlyph(string icon) => icon switch
    {
        "desktop" => "\uE7F4",
        "laptop" => "\uE770",
        "workstation" => "\uE950",
        "home" => "\uE80F",
        "tablet" => "\uE70A",
        "phone" => "\uE8EA",
        "server" => "\uE968",
        "cloud" => "\uE753",
        "office" => "\uE822",
        "briefcase" => "\uE821",
        "terminal" => "\uE756",
        "gaming" => "\uE7FC",
        "travel" => "\uE709",
        "school" => "\uE7BE",
        "studio" => "\uE7F6",
        "camera" => "\uE722",
        _ => throw new InvalidOperationException($"Unsupported installation icon '{icon}'.")
    };
}
