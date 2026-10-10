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

/// <summary>Presentation-only geometry for the main player's scroll-free viewport.</summary>
public static class MainPlayerResponsiveLayout
{
    /// <summary>Returns whether tracking and activity can be rendered side by side.</summary>
    public static bool UsesColumns(double contentWidth)
    {
        ValidateDimension(contentWidth);
        return contentWidth >= 944d;
    }

    /// <summary>Chooses full, compact, or summary detail using actual measured content heights.</summary>
    public static MainPlayerDetailLevel ResolveDetails(double viewportHeight, double fullHeight, double compactHeight)
    {
        ValidateDimension(viewportHeight);
        ValidateDimension(fullHeight);
        ValidateDimension(compactHeight);
        return fullHeight <= viewportHeight ? MainPlayerDetailLevel.Full
            : compactHeight <= viewportHeight ? MainPlayerDetailLevel.Compact
            : MainPlayerDetailLevel.Summary;
    }

    /// <summary>Grows the activity graph into spare space without exceeding the available viewport.</summary>
    public static double ResolveChartHeight(double viewportHeight, double measuredContentHeight, double baselineChartHeight)
    {
        ValidateDimension(viewportHeight);
        ValidateDimension(measuredContentHeight);
        ValidateDimension(baselineChartHeight);
        return baselineChartHeight + Math.Clamp(viewportHeight - measuredContentHeight, 0d, 126d);
    }

    private static void ValidateDimension(double value)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}

/// <summary>Responsive disclosure without changing the user's expanded-section preferences.</summary>
public enum MainPlayerDetailLevel
{
    /// <summary>All requested sections and captions are visible.</summary>
    Full,
    /// <summary>Compact controls, a short graph, and last-session summary are visible.</summary>
    Compact,
    /// <summary>The activity score replaces its histogram in the shortest viewports.</summary>
    Summary
}
