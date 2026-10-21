// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Renders saved activity intervals on an elapsed-time axis without performing I/O.</summary>
public sealed class ActivitySpanTimeline : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 6 };
    private readonly TextBlock _heading = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = new() { FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Visibility = Visibility.Collapsed };
    private readonly Canvas _axis = new() { Height = 50, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _detail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 5 };
    private IReadOnlyList<ActivitySpan> _spans = [];
    private LocalizationService _strings = new("system");
    private bool _vertical;
    private DateTimeOffset? _selectedTime;
    private string _emptyKey = "ActivityTimeline.Empty";

    /// <summary>Creates the shared passive activity rail.</summary>
    public ActivitySpanTimeline()
    {
        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_heading);
        Grid.SetColumn(_refresh, 1);
        header.Children.Add(_refresh);
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _root.Children.Add(header);
        _root.Children.Add(_axis);
        _root.Children.Add(_detail);
        Content = _root;
        _axis.SizeChanged += (_, _) => Render();
    }

    /// <summary>Occurs when a saved interval is selected.</summary>
    public event Action<ActivitySpan>? SpanInvoked;

    /// <summary>Requests a new local snapshot from the owning view.</summary>
    public event Action? RefreshRequested;

    /// <summary>Displays one local day as either a horizontal rail or a tall calendar column.</summary>
    public void SetItems(IReadOnlyList<ActivitySpan> spans, DateOnly date, LocalizationService strings, bool vertical = false,
        string? dayState = null)
    {
        _spans = spans;
        _strings = strings;
        _vertical = vertical;
        _refresh.Content = strings.Translate("Operations.Refresh");
        AutomationProperties.SetName(_refresh, strings.Translate("Operations.Refresh"));
        _refresh.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        _emptyKey = dayState is null or "not_generated" ? "ActivityTimeline.Empty"
            : dayState == "completed" ? "ActivityTimeline.NoIntervals" : "Timesheet.State." + dayState;
        _heading.Text = vertical ? date.ToString("ddd d MMM", strings.Culture) : strings.Translate("ActivityTimeline.Title");
        _axis.Height = spans.Count == 0 ? 0 : vertical ? 1152 : 58;
        _detail.Text = strings.Translate(spans.Count == 0 ? _emptyKey : "ActivityTimeline.Hint");
        AutomationProperties.SetName(this, _heading.Text);
        Render();
    }

    /// <summary>Shows a local loading or failure message while discarding old intervals.</summary>
    public void ShowMessage(string message)
    {
        _spans = [];
        _axis.Children.Clear();
        _axis.Height = 0;
        _heading.Text = "";
        _detail.Text = message;
    }

    /// <summary>Highlights the interval containing the selected capture, using half-open boundaries.</summary>
    public void SelectTime(DateTimeOffset? time)
    {
        _selectedTime = time;
        var selected = _spans.FirstOrDefault(s => time >= s.Start && time < s.End);
        _detail.Text = selected is null ? _strings.Translate(_spans.Count == 0 ? _emptyKey : "ActivityTimeline.Hint") : Label(selected);
        Render();
    }

    private string Label(ActivitySpan span) =>
        $"{span.Start:HH:mm:ss zzz}–{span.End:HH:mm:ss zzz} · {(span.End - span.Start).TotalMinutes.ToString("0.#", _strings.Culture)} min\n"
        + $"{Title(span)}{(span.Project.Length == 0 ? "" : " · " + span.Project)} · "
        + _strings.Translate(span.Confirmed ? "Timesheet.Confirmed" : "Timesheet.Draft")
        + " · " + _strings.Translate("Timesheet.Basis." + span.Basis)
        + " · " + _strings.Translate("Timesheet.Review." + span.ReviewState)
        + "\n" + _strings.Format("ActivityTimeline.Source", span.GeneratedAt.ToLocalTime().ToString("g", _strings.Culture));

    private string Title(ActivitySpan span) => span.Title.Length > 0 ? span.Title : _strings.Translate("Timesheet.Basis." + span.Basis);

    private void Render()
    {
        _axis.Children.Clear();
        if (_spans.Count == 0 || _axis.ActualWidth <= 0) return;
        var start = _spans[0].DayStart;
        var end = _spans[0].DayEnd;
        var seconds = (end - start).TotalSeconds;
        var extent = _vertical ? _axis.Height : _axis.ActualWidth;
        var accent = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
        var neutral = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ControlFillColorSecondaryBrush"];
        var foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        // Actual elapsed hours preserve repeated and skipped clock hours on DST transition days.
        for (var at = start; at <= end; at = at.AddHours(_vertical ? 1 : 4))
        {
            var tick = new TextBlock { Text = at.ToLocalTime().ToString("HH:mm", _strings.Culture), FontSize = 10, IsHitTestVisible = false };
            var position = (at - start).TotalSeconds / seconds * extent;
            Canvas.SetLeft(tick, _vertical ? 0 : Math.Min(position, Math.Max(0, extent - 34)));
            Canvas.SetTop(tick, _vertical ? Math.Min(position, extent - 14) : 42);
            _axis.Children.Add(tick);
        }
        foreach (var span in _spans)
        {
            var position = (span.Start - start).TotalSeconds / seconds * extent;
            var length = Math.Max(1, (span.End - span.Start).TotalSeconds / seconds * extent);
            var allocated = span.Basis is not ("unallocated" or "unavailable" or "overlap");
            var selected = _selectedTime >= span.Start && _selectedTime < span.End;
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = Title(span),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = _vertical ? TextWrapping.Wrap : TextWrapping.NoWrap
                },
                Width = _vertical ? Math.Max(1, _axis.ActualWidth - 38) : length,
                Height = _vertical ? length : 38,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(2),
                Background = allocated ? accent : neutral,
                Foreground = allocated ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"] : foreground,
                BorderBrush = foreground,
                BorderThickness = new Thickness(selected ? 2 : 1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            var label = Label(span);
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Click += (_, _) => { _detail.Text = label; _selectedTime = span.Start; Render(); SpanInvoked?.Invoke(span); };
            Canvas.SetLeft(button, _vertical ? 38 : position);
            Canvas.SetTop(button, _vertical ? position : 0);
            _axis.Children.Add(button);
        }
    }
}
