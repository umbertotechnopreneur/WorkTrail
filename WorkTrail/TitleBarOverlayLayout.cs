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

namespace WorkTrail;

/// <summary>Switches an astronomical widget's title bar between docked and overlay layouts without reparenting its content.</summary>
internal sealed class TitleBarOverlayLayout
{
    private readonly FrameworkElement _dragRegion;
    private readonly Grid _headerParent;
    private readonly RowDefinition _rootHeaderRow;
    private readonly RowDefinition _parentHeaderRow;
    private readonly GridLength _rootHeaderHeight;
    private readonly GridLength _parentHeaderHeight;
    private readonly double _originalHeight;
    private readonly VerticalAlignment _originalVerticalAlignment;
    private readonly int _originalRowSpan;
    private readonly int _originalZIndex;
    private readonly double _overlayHeight;
    private bool _enabled;

    /// <summary>Captures the widget's existing title-bar geometry for exact restoration when overlay mode is disabled.</summary>
    internal TitleBarOverlayLayout(Grid root, FrameworkElement dragRegion)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(dragRegion);
        _dragRegion = dragRegion;
        _headerParent = dragRegion.Parent as Grid
            ?? throw new ArgumentException("An overlay title bar must belong to a grid.", nameof(dragRegion));
        if (Grid.GetRow(dragRegion) != 0 || root.RowDefinitions.Count < 2 || _headerParent.RowDefinitions.Count < 2)
        {
            // Unsupported layouts fail before any mutation; their content must not be silently rearranged.
            throw new ArgumentException("An overlay title bar requires a first header row followed by content rows.", nameof(dragRegion));
        }

        if (!ReferenceEquals(_headerParent, root)
            && (!ReferenceEquals(_headerParent.Parent, root)
                || Grid.GetRow(_headerParent) != 0
                || Grid.GetRowSpan(_headerParent) < root.RowDefinitions.Count))
        {
            throw new ArgumentException("A nested title-bar grid must span every row of the window root.", nameof(root));
        }

        _rootHeaderRow = root.RowDefinitions[0];
        _parentHeaderRow = _headerParent.RowDefinitions[0];
        if (_rootHeaderRow.MinHeight != 0d || _parentHeaderRow.MinHeight != 0d
            || root.RowSpacing != 0d || _headerParent.RowSpacing != 0d)
        {
            throw new ArgumentException("Overlay title-bar rows must collapse without minimum height or row spacing.", nameof(root));
        }

        _rootHeaderHeight = _rootHeaderRow.Height;
        _parentHeaderHeight = _parentHeaderRow.Height;
        _originalHeight = dragRegion.Height;
        _originalVerticalAlignment = dragRegion.VerticalAlignment;
        _originalRowSpan = Grid.GetRowSpan(dragRegion);
        _originalZIndex = Canvas.GetZIndex(dragRegion);
        _overlayHeight = _parentHeaderHeight.IsAbsolute ? _parentHeaderHeight.Value : _originalHeight;
        if (!double.IsFinite(_overlayHeight) || _overlayHeight <= 0d)
        {
            throw new ArgumentException("An overlay title bar requires an explicit positive height.", nameof(dragRegion));
        }
    }

    /// <summary>Gets the space reserved above the widget content by the current title-bar layout.</summary>
    internal double ReservedHeight => _enabled ? 0d : _dragRegion.ActualHeight;

    /// <summary>Gets the fixed caption height used to bound the overlay's input surface.</summary>
    internal double HeaderHeight => _overlayHeight;

    /// <summary>Changes the layout mode while preserving the existing parent, resources, opacity, and docked geometry.</summary>
    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            // Span the content rows before collapsing the header row, keeping the overlay visible and bounded.
            _dragRegion.Height = _overlayHeight;
            _dragRegion.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRowSpan(_dragRegion, _headerParent.RowDefinitions.Count);
            Canvas.SetZIndex(_dragRegion, 200);
            _parentHeaderRow.Height = new GridLength(0d);
            _rootHeaderRow.Height = new GridLength(0d);
            return;
        }

        _rootHeaderRow.Height = _rootHeaderHeight;
        _parentHeaderRow.Height = _parentHeaderHeight;
        _dragRegion.Height = _originalHeight;
        _dragRegion.VerticalAlignment = _originalVerticalAlignment;
        Grid.SetRowSpan(_dragRegion, _originalRowSpan);
        Canvas.SetZIndex(_dragRegion, _originalZIndex);
    }
}
