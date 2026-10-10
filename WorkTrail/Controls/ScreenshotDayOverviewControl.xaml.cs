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
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Passively renders retained screenshots as selectable markers across an adaptive local-time window.</summary>
public sealed partial class ScreenshotDayOverviewControl : UserControl
{
    private const double MarkerBaselineInset = 6d;
    private ScreenshotDayTimelineState _state = ScreenshotDayTimelineState.Empty;
    private LocalizationService _strings = new("system");

    /// <summary>Creates the compact adaptive screenshot overview.</summary>
    public ScreenshotDayOverviewControl() => InitializeComponent();

    /// <summary>Occurs when the user selects a retained screenshot marker.</summary>
    public event Action<int>? SelectedIndexChanged;

    /// <summary>Replaces the complete day projection and its current selection.</summary>
    public void SetItems(IReadOnlyList<ScreenshotGalleryItem> items, int selectedIndex, string language)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        _strings = new LocalizationService(language);
        _state = ScreenshotDayTimelineProjection.Create(items, selectedIndex);
        DayOverviewRoot.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderAxisLabels();
        RenderSelectionRange();
        RenderMarkers();
    }

    private void MarkerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSelectionRange();
        RenderMarkers();
    }

    private void RenderAxisLabels()
    {
        if (_state.Markers.Count == 0)
        {
            return;
        }

        var labels = new[] { Tick0Text, Tick1Text, Tick2Text, Tick3Text, Tick4Text };
        if (_state.Ticks.Count != labels.Length)
        {
            throw new InvalidOperationException("The screenshot timeline projection must expose five axis ticks.");
        }

        for (var index = 0; index < labels.Length; index++)
        {
            labels[index].Text = FormatTimelineTime(_state.Ticks[index]);
        }
    }

    private void RenderSelectionRange()
    {
        if (_state.Markers.Count == 0 || MarkerCanvas.ActualWidth <= 0d)
        {
            SelectedRangeText.Text = "--";
            SelectedRangeLabel.Visibility = Visibility.Collapsed;
            SelectionRangeIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var culture = _strings.Culture;
        var anchorDate = DateTime.Today;
        var rangeStart = anchorDate.Add(_state.SelectionStart);
        var rangeEnd = anchorDate.Add(_state.SelectionEnd);
        SelectedRangeText.Text = $"{rangeStart.ToString("t", culture)}–{rangeEnd.ToString("t", culture)}";
        SelectedRangeLabel.Visibility = Visibility.Visible;

        var width = MarkerCanvas.ActualWidth;
        var windowDuration = _state.WindowEnd - _state.WindowStart;
        var start = (_state.SelectionStart - _state.WindowStart).TotalSeconds / windowDuration.TotalSeconds;
        var end = (_state.SelectionEnd - _state.WindowStart).TotalSeconds / windowDuration.TotalSeconds;
        start = Math.Clamp(start, 0d, 1d);
        end = Math.Clamp(end, start, 1d);
        var left = Math.Clamp(start * width, 0d, width);
        var selectionWidth = Math.Max(18d, Math.Clamp((end - start) * width, 0d, width - left));
        SelectionRangeIndicator.Width = Math.Min(selectionWidth, width - left);
        Canvas.SetLeft(SelectionRangeIndicator, left);
        Canvas.SetTop(
            SelectionRangeIndicator,
            Math.Max(0d, MarkerCanvas.ActualHeight - SelectionRangeIndicator.Height - MarkerBaselineInset));
        SelectionRangeIndicator.Visibility = Visibility.Visible;

        var labelWidth = SelectedRangeLabel.Width;
        var maximumLabelLeft = Math.Max(0d, width - labelWidth);
        var minimumLabelLeft = Math.Min(120d, maximumLabelLeft);
        var labelLeft = Math.Clamp(
            left + (SelectionRangeIndicator.Width / 2d) - (labelWidth / 2d),
            minimumLabelLeft,
            maximumLabelLeft);
        Canvas.SetLeft(SelectedRangeLabel, labelLeft);
        AutomationProperties.SetName(
            SelectionRangeIndicator,
            _strings.Format("Screenshots.DayOverview.SelectedRange", rangeStart, rangeEnd));
    }

    private void RenderMarkers()
    {
        MarkerCanvas.Children.Clear();
        var width = MarkerCanvas.ActualWidth;
        var height = MarkerCanvas.ActualHeight;
        if (_state.Markers.Count == 0 || width <= 0d || height <= 0d)
        {
            return;
        }

        foreach (var marker in _state.Markers.OrderBy(static marker => marker.IsSelected))
        {
            var markerWidth = marker.IsSelected
                ? 14d
                : Math.Clamp(5d + (marker.CaptureCount * 2d), 7d, 11d);
            var markerHeight = marker.IsSelected
                ? 28d
                : Math.Clamp(8d + (marker.NormalizedActivity * 16d), 8d, 24d);
            var left = Math.Clamp((marker.NormalizedPosition * width) - (markerWidth / 2d), 0d, width - markerWidth);
            var top = Math.Max(0d, height - markerHeight - MarkerBaselineInset);
            var activityText = marker.NormalizedActivity <= 0d
                ? "--"
                : marker.NormalizedActivity.ToString("P0", _strings.Culture);
            var accessibleName = _strings.Format(
                "Screenshots.DayOverview.ItemAccessible",
                marker.Index + 1,
                marker.CapturedAt,
                activityText);
            var captureCountText = marker.CaptureCount == 1
                ? _strings.Translate("Screenshots.Count.One")
                : _strings.Format("Screenshots.Count.Many", marker.CaptureCount);
            accessibleName = $"{accessibleName} · {captureCountText}";
            var button = new Button
            {
                Width = markerWidth,
                Height = markerHeight,
                Style = (Style)Resources[marker.IsSelected ? "SelectedDayMarkerButtonStyle" : "DayMarkerButtonStyle"],
                Tag = marker.Index
            };
            AutomationProperties.SetName(button, accessibleName);
            ToolTipService.SetToolTip(button, accessibleName);
            button.Click += MarkerButton_Click;
            Canvas.SetLeft(button, left);
            Canvas.SetTop(button, top);
            MarkerCanvas.Children.Add(button);
        }
    }

    private string FormatTimelineTime(TimeSpan time)
        => time == TimeSpan.FromDays(1)
            ? "24:00"
            : DateTime.Today.Add(time).ToString("t", _strings.Culture);

    private void MarkerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int selectedIndex })
        {
            SelectedIndexChanged?.Invoke(selectedIndex);
        }
    }
}
