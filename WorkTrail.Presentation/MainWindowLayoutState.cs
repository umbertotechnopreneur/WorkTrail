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

/// <summary>Identifies the single top-level surface currently rendered by the compact main window.</summary>
public enum MainWindowSurface
{
    /// <summary>The live tracking player is visible.</summary>
    Player,

    /// <summary>The application-options surface is visible.</summary>
    Options,

    /// <summary>The operations and diagnostics surface is visible.</summary>
    Operations
}

/// <summary>Identifies a player section whose visibility contributes to the measured window height.</summary>
public enum MainWindowLayoutSection
{
    /// <summary>The activity-score histogram is visible.</summary>
    ActivityScore,

    /// <summary>The latest-session details are visible.</summary>
    LastSession,

    /// <summary>The short-lived manual-snapshot deletion panel is visible.</summary>
    PendingSnapshot,

    /// <summary>The active-hours warning is visible.</summary>
    OutsideActiveHoursWarning
}

/// <summary>
/// Owns presentation-only main-window visibility state and the latest measured logical height.
/// The window measures its actual visible XAML content, rather than deriving size from per-panel constants.
/// </summary>
public sealed class MainWindowLayoutState
{
    private const int InitialLogicalHeight = 304;
    private const int PreferredSecondarySurfaceLogicalHeight = 520;
    private (int Width, int Height)? _manualSize;

    /// <summary>Gets the currently active top-level surface.</summary>
    public MainWindowSurface Surface { get; private set; } = MainWindowSurface.Player;

    /// <summary>Gets the last valid content height measured in WinUI logical pixels.</summary>
    public int LogicalHeight { get; private set; } = InitialLogicalHeight;

    /// <summary>Gets whether the activity-score section is visible.</summary>
    public bool IsActivityScoreVisible { get; private set; } = true;

    /// <summary>Gets whether the last-session section is visible.</summary>
    public bool IsLastSessionVisible { get; private set; }

    /// <summary>Gets whether the pending snapshot section is visible.</summary>
    public bool IsPendingSnapshotVisible { get; private set; }

    /// <summary>Gets whether the active-hours warning is visible.</summary>
    public bool IsOutsideActiveHoursWarningVisible { get; private set; }

    /// <summary>Shows one top-level surface without resetting any player section state.</summary>
    /// <param name="surface">Surface to make current.</param>
    public void ShowSurface(MainWindowSurface surface)
    {
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        Surface = surface;
    }

    /// <summary>Changes a player section visibility and returns whether the state changed.</summary>
    /// <param name="section">Player section to update.</param>
    /// <param name="isVisible">Whether the section is visible.</param>
    /// <returns><see langword="true"/> when a new state was applied.</returns>
    public bool SetSectionVisibility(MainWindowLayoutSection section, bool isVisible)
    {
        var current = IsSectionVisible(section);
        if (current == isVisible)
        {
            return false;
        }

        switch (section)
        {
            case MainWindowLayoutSection.ActivityScore:
                IsActivityScoreVisible = isVisible;
                break;
            case MainWindowLayoutSection.LastSession:
                IsLastSessionVisible = isVisible;
                break;
            case MainWindowLayoutSection.PendingSnapshot:
                IsPendingSnapshotVisible = isVisible;
                break;
            case MainWindowLayoutSection.OutsideActiveHoursWarning:
                IsOutsideActiveHoursWarningVisible = isVisible;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section));
        }

        return true;
    }

    /// <summary>Toggles a player section and returns its new visibility.</summary>
    /// <param name="section">Player section to toggle.</param>
    /// <returns>The new section visibility.</returns>
    public bool ToggleSection(MainWindowLayoutSection section)
    {
        var isVisible = !IsSectionVisible(section);
        _ = SetSectionVisibility(section, isVisible);
        return isVisible;
    }

    /// <summary>Records a measured logical content height and preserves the prior value during transient zero-size layout passes.</summary>
    /// <param name="measuredHeight">Measured XAML content height in logical pixels.</param>
    /// <returns>The current valid logical window height.</returns>
    public int RecordMeasuredHeight(double measuredHeight)
    {
        if (double.IsNaN(measuredHeight) || double.IsInfinity(measuredHeight) || measuredHeight <= 0d)
        {
            return LogicalHeight;
        }

        LogicalHeight = checked((int)Math.Ceiling(measuredHeight));
        return LogicalHeight;
    }

    /// <summary>Retains the user's resized bounds for the entire main window across all surfaces in logical pixels.</summary>
    public void RecordManualSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!double.IsFinite(height) || height <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _manualSize = (checked((int)Math.Ceiling(width)), checked((int)Math.Ceiling(height)));
    }

    /// <summary>Fits the main window's manual width, or the surface's preferred width, within the current display.</summary>
    public int ResolveLogicalWidth(double availableLogicalWidth, int preferredLogicalWidth)
    {
        if (!double.IsFinite(availableLogicalWidth) || availableLogicalWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(availableLogicalWidth));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(preferredLogicalWidth);
        var width = _manualSize?.Width ?? preferredLogicalWidth;
        return Math.Min(width, Math.Max(1, checked((int)Math.Floor(availableLogicalWidth))));
    }

    /// <summary>Constrains the measured height and requested outer padding to the current display and active surface.</summary>
    /// <param name="availableLogicalHeight">Usable display height in WinUI logical pixels after outer margins.</param>
    /// <param name="additionalLogicalHeight">Extra logical pixels reserved outside the measured content.</param>
    /// <returns>The logical window height to apply.</returns>
    public int ResolveLogicalHeight(double availableLogicalHeight, int additionalLogicalHeight)
    {
        if (double.IsNaN(availableLogicalHeight) || double.IsInfinity(availableLogicalHeight) || availableLogicalHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(availableLogicalHeight));
        }

        if (additionalLogicalHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(additionalLogicalHeight));
        }

        var displayLimit = Math.Max(1, checked((int)Math.Floor(availableLogicalHeight)));
        if (_manualSize is { } manualSize)
        {
            // Navigation preserves the user's viewport; the player reflows and reduces secondary detail to fit.
            return Math.Min(manualSize.Height, displayLimit);
        }

        var surfaceLimit = Surface == MainWindowSurface.Player
            ? displayLimit
            : Math.Min(displayLimit, PreferredSecondarySurfaceLogicalHeight);
        return Math.Min(checked(LogicalHeight + additionalLogicalHeight), surfaceLimit);
    }

    private bool IsSectionVisible(MainWindowLayoutSection section) => section switch
    {
        MainWindowLayoutSection.ActivityScore => IsActivityScoreVisible,
        MainWindowLayoutSection.LastSession => IsLastSessionVisible,
        MainWindowLayoutSection.PendingSnapshot => IsPendingSnapshotVisible,
        MainWindowLayoutSection.OutsideActiveHoursWarning => IsOutsideActiveHoursWarningVisible,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };
}
