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

/// <summary>Describes the localized phase name and illuminated lunar-disc percentage.</summary>
public sealed record LunarPhasePresentation(
    string LocalizationKey,
    double IlluminatedPercentage,
    string Glyph);

/// <summary>Projects the astronomy phase angle into stable presentation data.</summary>
public static class LunarPhaseProjection
{
    /// <summary>Creates an eight-phase presentation and a rounded illuminated percentage.</summary>
    public static LunarPhasePresentation Create(double phaseAngleDegrees)
    {
        if (!double.IsFinite(phaseAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(phaseAngleDegrees));
        }

        var normalized = (phaseAngleDegrees % 360d + 360d) % 360d;
        var phaseIndex = (int)Math.Floor((normalized + 22.5d) / 45d) % 8;
        var (localizationKey, glyph) = phaseIndex switch
        {
            0 => ("WorldClock.MoonPhase.New", "🌑"),
            1 => ("WorldClock.MoonPhase.WaxingCrescent", "🌒"),
            2 => ("WorldClock.MoonPhase.FirstQuarter", "🌓"),
            3 => ("WorldClock.MoonPhase.WaxingGibbous", "🌔"),
            4 => ("WorldClock.MoonPhase.Full", "🌕"),
            5 => ("WorldClock.MoonPhase.WaningGibbous", "🌖"),
            6 => ("WorldClock.MoonPhase.LastQuarter", "🌗"),
            7 => ("WorldClock.MoonPhase.WaningCrescent", "🌘"),
            _ => throw new InvalidDataException("The lunar phase index is outside the supported range.")
        };
        var illuminatedPercentage = Math.Round(
            (1d - Math.Cos(normalized * Math.PI / 180d)) * 50d,
            MidpointRounding.AwayFromZero);
        return new LunarPhasePresentation(localizationKey, illuminatedPercentage, glyph);
    }
}
