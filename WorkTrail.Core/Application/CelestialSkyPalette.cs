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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkTrail.Application;

/// <summary>Provides decorative sky colors and visibility weights for a calculated solar altitude.</summary>
public sealed record CelestialSkyAppearance(
    string ZenithColor, string UpperSkyColor, string HorizonColor, double StarOpacity, double SunGlowOpacity,
    string SunGlowCoreColor, string SunGlowEdgeColor);

/// <summary>Interpolates twenty-four artistic skies against actual solar elevation, including polar day/night.</summary>
public static class CelestialSkyPalette
{
    private static readonly Lazy<PaletteCatalog> Palette = new(LoadPalette);

    /// <summary>Number of sky keyframes in the embedded palette.</summary>
    public static int PaletteCount => Palette.Value.Frames.Count;

    private static PaletteCatalog LoadPalette()
    {
        // Missing product artwork data fails explicitly; no hard-coded color fallback is maintained.
        using var stream = typeof(CelestialSkyPalette).Assembly.GetManifestResourceStream("WorkTrail.Data.sky-palette.json")
            ?? throw new InvalidDataException("The embedded sky palette is missing.");
        var palette = JsonSerializer.Deserialize<PaletteCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("The embedded sky palette is empty.");
        if (palette.SchemaVersion != 1 || palette.Frames is null || palette.Frames.Count < 2
            || !ValidColor(palette.SunGlowCoreColor, 8) || !ValidColor(palette.SunGlowEdgeColor, 8))
            throw new InvalidDataException("The sky palette schema is unsupported.");
        var previousAltitude = double.NegativeInfinity;
        foreach (var frame in palette.Frames)
        {
            if (frame is null || !double.IsFinite(frame.AltitudeDegrees)
                || frame.AltitudeDegrees is < -90 or > 90 || frame.AltitudeDegrees <= previousAltitude
                || !ValidColor(frame.ZenithColor, 6) || !ValidColor(frame.UpperSkyColor, 6) || !ValidColor(frame.HorizonColor, 6)
                || !double.IsFinite(frame.StarOpacity) || frame.StarOpacity is < 0 or > 1
                || !double.IsFinite(frame.SunGlowOpacity) || frame.SunGlowOpacity is < 0 or > 1)
                throw new InvalidDataException("The sky palette contains an invalid frame.");
            previousAltitude = frame.AltitudeDegrees;
        }
        if (palette.Frames[0].AltitudeDegrees != -90 || palette.Frames[^1].AltitudeDegrees != 90)
            throw new InvalidDataException("The sky palette must span the physical solar altitude range.");
        return palette;
    }

    private static bool ValidColor(string? value, int length) => value is not null && value.Length == length
        && value.All(static character => char.IsAsciiHexDigit(character));

    /// <summary>Creates a continuous palette; invalid elevations fail instead of inventing a sky state.</summary>
    public static CelestialSkyAppearance Create(double solarAltitudeDegrees)
    {
        if (!double.IsFinite(solarAltitudeDegrees) || solarAltitudeDegrees is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(solarAltitudeDegrees), "Solar altitude must be finite and within the physical horizon range.");

        var frames = Palette.Value.Frames;
        var upperIndex = 0;
        while (upperIndex < frames.Count - 1 && frames[upperIndex].AltitudeDegrees < solarAltitudeDegrees) upperIndex++;
        var lower = frames[Math.Max(0, upperIndex - 1)];
        var upper = frames[upperIndex];
        var blend = upper.AltitudeDegrees == lower.AltitudeDegrees ? 0 : (solarAltitudeDegrees - lower.AltitudeDegrees) / (upper.AltitudeDegrees - lower.AltitudeDegrees);
        return new CelestialSkyAppearance(
            BlendColor(lower.Zenith, upper.Zenith, blend),
            BlendColor(lower.UpperSky, upper.UpperSky, blend),
            BlendColor(lower.Horizon, upper.Horizon, blend),
            lower.StarOpacity + (upper.StarOpacity - lower.StarOpacity) * blend,
            lower.SunGlowOpacity + (upper.SunGlowOpacity - lower.SunGlowOpacity) * blend,
            Palette.Value.SunGlowCoreColor, Palette.Value.SunGlowEdgeColor);
    }

    private static string BlendColor(string first, string second, double blend)
    {
        var channels = new byte[3];
        for (var channel = 0; channel < 3; channel++)
        {
            var a = byte.Parse(first.AsSpan(channel * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(second.AsSpan(channel * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            channels[channel] = (byte)Math.Round(a + (b - a) * blend);
        }
        return $"#{channels[0]:X2}{channels[1]:X2}{channels[2]:X2}";
    }

    private sealed record PaletteCatalog(int SchemaVersion, string SunGlowCoreColor, string SunGlowEdgeColor, IReadOnlyList<Frame> Frames);
    private sealed record Frame(double AltitudeDegrees, string ZenithColor, string UpperSkyColor, string HorizonColor,
        double StarOpacity, double SunGlowOpacity)
    {
        internal string Zenith => ZenithColor;
        internal string UpperSky => UpperSkyColor;
        internal string Horizon => HorizonColor;
    }
}
