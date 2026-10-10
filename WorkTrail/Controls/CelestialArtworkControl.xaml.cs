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
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;

namespace WorkTrail.Controls;

/// <summary>Identifies a decorative, non-measurement illustration in the four-column celestial atlas.</summary>
public enum CelestialArtworkKind
{
    /// <summary>Mercury illustration.</summary>
    Mercury,
    /// <summary>Venus illustration.</summary>
    Venus,
    /// <summary>Mars illustration.</summary>
    Mars,
    /// <summary>Jupiter illustration.</summary>
    Jupiter,
    /// <summary>Saturn illustration.</summary>
    Saturn,
    /// <summary>Uranus illustration.</summary>
    Uranus,
    /// <summary>Neptune illustration.</summary>
    Neptune,
    /// <summary>Sunrise illustration.</summary>
    Sunrise,
    /// <summary>Sunset illustration.</summary>
    Sunset,
    /// <summary>Blue-hour illustration.</summary>
    BlueHour,
    /// <summary>Civil-twilight illustration.</summary>
    Twilight,
    /// <summary>Equinox and solstice illustration.</summary>
    Seasons
}

/// <summary>Displays a reusable atlas thumbnail through a clipped native image viewport, without asset I/O or scientific calculations.</summary>
public sealed partial class CelestialArtworkControl : UserControl
{
    private const double CellSize = 96d;

    /// <summary>Identifies the selected decorative atlas cell.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(CelestialArtworkKind), typeof(CelestialArtworkControl),
        new PropertyMetadata(CelestialArtworkKind.Mercury, OnKindChanged));

    /// <summary>Creates one lightweight photographic-style thumbnail.</summary>
    public CelestialArtworkControl()
    {
        InitializeComponent();
        UpdateCell();
    }

    /// <summary>Gets or sets the atlas illustration; positions and visibility always come from the astronomical DTO.</summary>
    public CelestialArtworkKind Kind
    {
        get => (CelestialArtworkKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Maps an astronomical planet identifier to its presentation-only illustration.</summary>
    internal static CelestialArtworkKind ForPlanet(CelestialBodyKind body) => body switch
    {
        CelestialBodyKind.Mercury => CelestialArtworkKind.Mercury,
        CelestialBodyKind.Venus => CelestialArtworkKind.Venus,
        CelestialBodyKind.Mars => CelestialArtworkKind.Mars,
        CelestialBodyKind.Jupiter => CelestialArtworkKind.Jupiter,
        CelestialBodyKind.Saturn => CelestialArtworkKind.Saturn,
        CelestialBodyKind.Uranus => CelestialArtworkKind.Uranus,
        CelestialBodyKind.Neptune => CelestialArtworkKind.Neptune,
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, "The Sun and Moon use their existing photographed phase control.")
    };

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CelestialArtworkControl)sender).UpdateCell();

    private void UpdateCell()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown celestial atlas cell.");
        }

        var index = (int)Kind;
        // Position the 4-by-3 atlas behind a one-cell clip; the Viewbox scales the already-cropped result.
        Canvas.SetLeft(AtlasImage, -(index % 4) * CellSize);
        Canvas.SetTop(AtlasImage, -(index / 4) * CellSize);
        ThumbnailFrame.CornerRadius = new CornerRadius(index < 7 && Kind != CelestialArtworkKind.Saturn ? 48 : 18);
    }
}
