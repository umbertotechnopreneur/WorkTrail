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

/// <summary>Shares column widths and bounded row geometry between the monitor header and its devices.</summary>
public sealed record SensorTrackLayout(bool Stacked, double NameWidth, double ValueWidth, double TemperatureWidth,
    double Padding, double RowHeight);

/// <summary>Keeps identity, capacity and temperature readable by paging short or narrow windows.</summary>
public static class SensorMonitorLayout
{
    /// <summary>Computes aligned desktop columns or two compact lines over a background trace.</summary>
    public static SensorTrackLayout ResolveTrack(double width)
    {
        Validate(width);
        var stacked = width < 860;
        var valueWidth = stacked ? Math.Max(0, (width - 60) / 2) : Math.Clamp(width * .18, 116, 160);
        var temperatureWidth = stacked ? valueWidth : Math.Clamp(width * .20, 132, 180);
        // Compact metrics share a second line after the 44 px icon indent and a 16 px gutter.
        var nameWidth = stacked ? width - 44 : width - valueWidth - temperatureWidth - 76;
        return new(stacked, Math.Max(0, nameWidth), valueWidth,
            temperatureWidth, 10, stacked ? 148 : 96);
    }

    /// <summary>Pages using measured row height, so wrapped or enlarged text is never squeezed into a nominal row.</summary>
    public static int PageSize(double viewportHeight, double rowHeight, int deviceCount)
    {
        Validate(viewportHeight);
        Validate(rowHeight);
        ArgumentOutOfRangeException.ThrowIfZero(rowHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(deviceCount);
        return Math.Min(deviceCount, Math.Max(1, (int)(viewportHeight / rowHeight)));
    }

    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
}
