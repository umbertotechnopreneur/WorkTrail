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
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Renders installation accents only when they fit inside an activity cell.</summary>
internal sealed class ActivityInstallationBadges : StackPanel
{
    private const double BadgeSize = 16;
    private const double BadgeSpacing = 3;
    private readonly Border[] _badges;
    private readonly CultureInfo _culture;
    private readonly TextBlock _overflow = new() { FontSize = 10, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center };
    private int _visibleCount = -1;
    private int _hiddenCount = -1;

    /// <summary>Creates decorative badges; the owning cell supplies the complete accessible description.</summary>
    internal ActivityInstallationBadges(IReadOnlyList<InstallationProfile> profiles, CultureInfo culture)
    {
        _culture = culture;
        Orientation = Orientation.Horizontal;
        Spacing = BadgeSpacing;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _badges = profiles.Select(CreateBadge).ToArray();
    }

    private static Border CreateBadge(InstallationProfile profile)
    {
        var accent = InstallationAppearance.CreateAccentBrush(profile.Color);
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Linear(accent.Color.R) + 0.7152 * Linear(accent.Color.G) + 0.0722 * Linear(accent.Color.B);
        return new Border
        {
            Width = BadgeSize,
            Height = BadgeSize,
            CornerRadius = new CornerRadius(BadgeSize / 2),
            Background = accent,
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Glyph = InstallationAppearance.GetIconGlyph(profile.Icon),
                Foreground = new SolidColorBrush(luminance > 0.179 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White),
                IsTextScaleFactorEnabled = false
            }
        };
    }

    /// <summary>Fits badges and an optional overflow count without growing or clipping the owning cell.</summary>
    internal void UpdateAvailableSize(double width, double height)
    {
        var count = height >= 20 && width >= BadgeSize
            ? Math.Min(_badges.Length, (int)Math.Floor((width + BadgeSpacing) / (BadgeSize + BadgeSpacing)))
            : 0;
        var hidden = 0;
        if (count > 0 && count < _badges.Length)
        {
            while (count > 0)
            {
                hidden = _badges.Length - count;
                _overflow.Text = "+" + hidden.ToString("N0", _culture);
                _overflow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (count * (BadgeSize + BadgeSpacing) + _overflow.DesiredSize.Width <= width
                    && _overflow.DesiredSize.Height <= height)
                {
                    break;
                }

                count--;
            }
        }

        // A cell too small for one icon and its overflow indication retains provenance in its tooltip.
        if (count == 0) hidden = 0;
        if (_visibleCount == count && _hiddenCount == hidden) return;
        _visibleCount = count;
        _hiddenCount = hidden;
        Children.Clear();
        foreach (var badge in _badges.Take(count)) Children.Add(badge);
        if (hidden > 0) Children.Add(_overflow);
        Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
