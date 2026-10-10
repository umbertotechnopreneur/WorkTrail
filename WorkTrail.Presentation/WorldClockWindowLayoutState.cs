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


using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Identifies the single top-level surface rendered by the detached world-clock window.</summary>
public enum WorldClockWindowSurface
{
    /// <summary>The equal-column clock comparison is visible.</summary>
    Clocks,

    /// <summary>The world-clock options surface is visible.</summary>
    Options
}

/// <summary>Describes the width projection for an equal-column world-clock surface.</summary>
public sealed record WorldClockColumnsLayout(double MinimumWidth, double Width);

/// <summary>Constrains reference-editor content inside the flyout border and the current XAML root.</summary>
public sealed record WorldClockReferenceFlyoutBounds(double ContentWidth, double ContentMaxHeight);

/// <summary>Describes the preferred and minimum logical bounds for a responsive world-clock surface.</summary>
public sealed record WorldClockWindowSizing(
    int PreferredLogicalWidth,
    int PreferredLogicalHeight,
    int MinimumLogicalWidth,
    int MinimumLogicalHeight);

/// <summary>Describes a sizing change and whether saved user bounds must be retained.</summary>
public sealed record WorldClockWindowResizeRequest(WorldClockWindowSizing Sizing, bool ResizeToPreferred);

/// <summary>Identifies the details that fit the measured city content in the current viewport.</summary>
public enum WorldClockDetailLevel
{
    /// <summary>Includes the solar arc and lunar information.</summary>
    Expanded,
    /// <summary>Includes time, weather and daylight duration.</summary>
    Summary,
    /// <summary>Prioritizes time, weather, date relation and UTC offset.</summary>
    Essential
}

/// <summary>Describes the mode rollback required after a failed reference-time conversion.</summary>
public sealed record WorldClockConversionFailureState(
    bool IsLive,
    bool CustomProjectionValid,
    bool RestoreLastSnapshotControls);

/// <summary>Owns the active world-clock surface and projects timer and viewport state.</summary>
public sealed class WorldClockWindowLayoutState
{
    /// <summary>Gets the packaged skyline width used to retain native scene detail when decoding layers.</summary>
    public const int ScenePixelWidth = 1280;
    private const double ScenePixelHeight = 720d;
    private static readonly TimeSpan MinuteBoundaryMargin = TimeSpan.FromMilliseconds(100);
    private WorldClockWindowSizing? _appliedWindowSizing;
    private bool _preserveRestoredSize;

    /// <summary>Gets the currently active top-level surface.</summary>
    public WorldClockWindowSurface Surface { get; private set; } = WorldClockWindowSurface.Clocks;

    /// <summary>Shows one top-level surface without resetting the current clock projection.</summary>
    /// <param name="surface">Surface to make current.</param>
    public void ShowSurface(WorldClockWindowSurface surface)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        Surface = surface;
    }

    /// <summary>Returns the remaining delay until the displayed snapshot's next UTC minute, refreshing immediately when overdue.</summary>
    /// <param name="instant">Reference instant of the currently displayed live snapshot.</param>
    /// <param name="now">Current UTC time at scheduling, including time elapsed since the snapshot was received.</param>
    public static TimeSpan DelayUntilNextMinute(DateTimeOffset instant, DateTimeOffset now)
    {
        var ticksIntoMinute = instant.UtcDateTime.Ticks % TimeSpan.TicksPerMinute;
        var ticksUntilNextMinute = TimeSpan.TicksPerMinute - ticksIntoMinute;
        var remaining = TimeSpan.FromTicks(ticksUntilNextMinute) + MinuteBoundaryMargin - (now - instant);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
    }

    /// <summary>Calculates an equal-column width that scrolls instead of compressing narrow content.</summary>
    public static WorldClockColumnsLayout CalculateColumnsLayout(
        int clockCount,
        double viewportWidth,
        double minimumColumnWidth = 320d)
    {
        if (clockCount is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(clockCount), clockCount, "World clocks support one through twelve columns.");
        }

        if (!double.IsFinite(viewportWidth) || viewportWidth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), viewportWidth, "Viewport width must be finite and non-negative.");
        }

        if (!double.IsFinite(minimumColumnWidth) || minimumColumnWidth < 320d)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumColumnWidth));
        }

        var minimumWidth = minimumColumnWidth * clockCount;
        // Fill the viewport even for one or two cities; scroll only below the readable column width.
        return new WorldClockColumnsLayout(minimumWidth, Math.Max(minimumWidth, viewportWidth));
    }

    /// <summary>Caps vertical scene growth at native resolution, allowing only the enlargement needed to cover the column width.</summary>
    public static double CalculateSceneHeight(double columnWidth, double columnHeight, double rasterizationScale)
    {
        if (!double.IsFinite(columnWidth) || columnWidth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(columnWidth));
        }

        if (!double.IsFinite(columnHeight) || columnHeight < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(columnHeight));
        }

        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        }

        // Taller windows add empty space above the scene. Very wide columns still fill edge to edge.
        var maximumHeight = Math.Max(ScenePixelHeight / rasterizationScale, columnWidth * (ScenePixelHeight / ScenePixelWidth));
        return Math.Min(columnHeight, maximumHeight);
    }

    /// <summary>Calculates content-led bounds so a small city set stays compact without restricting manual resize.</summary>
    public static WorldClockWindowSizing CalculateWindowSizing(int clockCount)
    {
        if (clockCount is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(clockCount), clockCount, "World clocks support one through twelve columns.");
        }

        var preferredWidth = clockCount switch
        {
            1 => 480,
            2 => 780,
            _ => 1120
        };
        var minimum = WindowStateService.GetMinimumSize(WindowStateKeys.WorldClocks);
        return new(preferredWidth, 680,
            minimum.Width, minimum.Height);
    }

    /// <summary>Reserves a 12-DIP root inset and the flyout border so neither axis can clip its content.</summary>
    public static WorldClockReferenceFlyoutBounds CalculateReferenceFlyoutBounds(double rootWidth, double rootHeight)
    {
        if (!double.IsFinite(rootWidth) || rootWidth <= 26d)
        {
            throw new ArgumentOutOfRangeException(nameof(rootWidth));
        }

        if (!double.IsFinite(rootHeight) || rootHeight <= 26d)
        {
            throw new ArgumentOutOfRangeException(nameof(rootHeight));
        }

        return new(Math.Min(480d, rootWidth - 24d) - 2d, Math.Min(720d, rootHeight - 24d) - 2d);
    }

    /// <summary>Preserves successfully restored manual bounds on the first populated snapshot.</summary>
    public void SetPlacementRestored(bool restored) => _preserveRestoredSize = restored;

    /// <summary>Returns a pending sizing change, deferring it while options are visible.</summary>
    public WorldClockWindowResizeRequest? GetWindowResizeRequest(int clockCount)
    {
        var sizing = CalculateWindowSizing(clockCount);
        return Surface == WorldClockWindowSurface.Options || sizing == _appliedWindowSizing
            ? null
            : new(sizing, ResizeToPreferred: !_preserveRestoredSize);
    }

    /// <summary>Records sizing only after the view has successfully applied or retained the bounds.</summary>
    public void AcceptWindowResizeRequest(WorldClockWindowResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appliedWindowSizing = request.Sizing;
        _preserveRestoredSize = false;
    }

    /// <summary>Starts a new content sizing cycle after the final city has been removed.</summary>
    public void ResetWindowSizing()
    {
        _appliedWindowSizing = null;
        _preserveRestoredSize = false;
    }

    /// <summary>Discloses detail only when its measured height fits; the explicit compact choice never reveals the arc.</summary>
    public static WorldClockDetailLevel CalculateDetailLevel(
        double viewportHeight,
        double expandedContentHeight,
        double summaryContentHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(expandedContentHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(summaryContentHeight);
        if (!double.IsFinite(viewportHeight) || !double.IsFinite(expandedContentHeight) || !double.IsFinite(summaryContentHeight))
        {
            throw new ArgumentException("Viewport and measured content heights must be finite.");
        }

        return viewportHeight >= expandedContentHeight
            ? WorldClockDetailLevel.Expanded
            : viewportHeight >= summaryContentHeight ? WorldClockDetailLevel.Summary : WorldClockDetailLevel.Essential;
    }

    /// <summary>Keeps an existing explicit projection, or transactionally restores a live projection.</summary>
    public static WorldClockConversionFailureState ResolveConversionFailure(bool transitionStartedFromLive) =>
        transitionStartedFromLive
            ? new WorldClockConversionFailureState(
                IsLive: true,
                CustomProjectionValid: true,
                RestoreLastSnapshotControls: true)
            : new WorldClockConversionFailureState(
                IsLive: false,
                CustomProjectionValid: false,
                RestoreLastSnapshotControls: false);
}
