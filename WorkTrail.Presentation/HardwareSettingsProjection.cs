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
using WorkTrail.Services;

namespace WorkTrail.Presentation;

/// <summary>Contains localized sensor sampling information for a settings slider.</summary>
public sealed record HardwareSamplingViewState(string Label, string Summary, string Detail);

/// <summary>Formats the shared sampling catalog without owning sampling or persistence behavior.</summary>
public static class HardwareSettingsProjection
{
    /// <summary>Resolves an integral slider position to a shared catalog key.</summary>
    public static string ProfileKeyAt(double position)
    {
        if (!double.IsFinite(position) || position != Math.Truncate(position) || position < 0 || position >= HardwareSamplingProfiles.All.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return HardwareSamplingProfiles.All[(int)position].Key;
    }

    /// <summary>Finds a validated catalog key's position without introducing a second profile list.</summary>
    public static int ProfileIndex(string key)
    {
        var profile = HardwareSamplingProfiles.Get(key);
        return HardwareSamplingProfiles.All.ToList().FindIndex(candidate => candidate.Key == profile.Key);
    }

    /// <summary>Formats the selected profile's actual polling intervals for the current language.</summary>
    public static HardwareSamplingViewState Create(string key, CultureInfo culture, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(translate);
        var profile = HardwareSamplingProfiles.Get(key);
        return new HardwareSamplingViewState(
            translate("Options.Sensors.Profile." + profile.Key),
            string.Format(culture, translate("Options.Sensors.Intervals"),
                Seconds(profile.CpuInterval, culture), Seconds(profile.MemoryInterval, culture), Seconds(profile.BatteryInterval, culture)),
            string.Format(culture, translate("Options.Sensors.Intervals.More"),
                Seconds(profile.CpuInterval, culture), Seconds(profile.StorageInterval, culture), Seconds(profile.NetworkInterval, culture)));
    }

    private static string Seconds(TimeSpan interval, CultureInfo culture) => interval.TotalSeconds.ToString("0.#", culture);
}
