// SPDX-License-Identifier: MIT
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;

namespace WorkTrail;

/// <summary>Renders chronological Core DTOs and collects optimistic corrections without local I/O.</summary>
internal sealed partial class ReportExportWindow
{
    private bool _timesheetEditing;
    private int _timesheetEditRevision;
    private TimesheetSegment? _timesheetEditingSegment;
    private ScreenshotWindow? _timesheetEvidenceWindow;
    private static readonly string[] TimesheetEditBases = ["manual", "unallocated", "unavailable"];

    private void InitializeTimesheetReview()
    {
        foreach (var (box, key) in new[] { (TimesheetEditStart, "Start"), (TimesheetEditEnd, "End"), (TimesheetEditTask, "Task"),
            (TimesheetEditProject, "Project"), (TimesheetEditNote, "Notes"), (TimesheetSplitAt, "SplitAt") })
        {
            box.Header = T("Timesheet." + key);
            UiLocalization.SetAccessibleLabel(box, T("Timesheet." + key));
        }
        TimesheetEditBasis.Header = T("Timesheet.Basis");
        TimesheetAlternatives.Header = T("Timesheet.Attribution");
        TimesheetEditBasis.ItemsSource = TimesheetEditBases.Select(b => T("Timesheet.Basis." + b)).ToArray();
        UiLocalization.SetAccessibleLabel(TimesheetEditBasis, T("Timesheet.Basis"));
        UiLocalization.SetAccessibleLabel(TimesheetAlternatives, T("Timesheet.Attribution"));
        UiLocalization.SetAccessibleLabel(TimesheetSegments, T("Timesheet.Segments"));
        UiLocalization.SetAccessibleLabel(TimesheetEvidenceList, T("Timesheet.EvidenceSheet"));
    }

    private void RenderTimesheetReview(TimesheetBatchView view)
    {
        var tasks = (view.Tasks ?? []).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(view.TimeZoneId ?? "UTC");
        var selectedId = _timesheetEditingSegment?.Id;
        var items = (view.Segments ?? []).Select(s => new SegmentPresentation(s,
            $"{TimeZoneInfo.ConvertTime(s.Start, zone):yyyy-MM-dd HH:mm:ss zzz} — {TimeZoneInfo.ConvertTime(s.End, zone):HH:mm:ss zzz} · {Duration(s.Seconds)}",
            $"{(s.Project.Length == 0 ? T("Timesheet.Unassigned") : s.Project)} · {(s.TaskId is { } id && tasks.TryGetValue(id, out var task) ? task.Title : T("Timesheet.Basis." + s.Basis))} · {T("Timesheet.Review." + s.ReviewState)} · {T("Timesheet.Basis." + s.Basis)}")).ToArray();
        TimesheetSegments.ItemsSource = items;
        TimesheetSegments.SelectedItem = items.FirstOrDefault(i => i.Segment.Id == selectedId);
        TimesheetRetryButton.IsEnabled = view.Selected?.ResultsSaved == true && !view.Historical && view.Days?.Any(d => d.State != "completed") == true;
        TimesheetConfirmButton.IsEnabled = view.Selected?.ResultsSaved == true && !view.Historical && !view.Confirmed;
        TimesheetAddButton.IsEnabled = view.Selected?.ResultsSaved == true && !view.Historical;
        TimesheetSegmentsPrevious.IsEnabled = view.SegmentPage > 0;
        TimesheetSegmentsNext.IsEnabled = (view.SegmentPage + 1) * 200 < view.SegmentCount;
        TimesheetDayStatus.Text = view.Historical ? T("Timesheet.Historical") : string.Join("\n", (view.Days ?? []).Select(d =>
            $"{d.Date:yyyy-MM-dd} · {T("Timesheet.State." + d.State)} · {d.ObservationCount} {T("Timesheet.Sources")}" +
            (d.AssumedCadence ? " · " + T("Timesheet.AssumedCadence") : "") + (d.FailureCode is { } code ? " · " + T(code) : "")));
        TimesheetTotals.Text = view.Coverage is not { } coverage ? "" :
            T(view.Confirmed ? "Timesheet.Confirmed" : "Timesheet.Draft") + " · " + _strings.Format("Timesheet.Coverage",
                Duration(coverage.WorkSeconds), Duration(coverage.UnallocatedSeconds), Duration(coverage.UnavailableSeconds), Duration(coverage.OverlapSeconds))
            + (coverage.FirstObservation is { } first && coverage.LastObservation is { } last
                ? $"\n{T("Timesheet.ObservationSpan")}: {TimeZoneInfo.ConvertTime(first, zone):g} — {TimeZoneInfo.ConvertTime(last, zone):g} · {zone.Id}" : "");
        if (view.SupportingCaptures is { } captures)
            TimesheetEvidenceList.ItemsSource = captures.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => new EvidencePresentation(g.First(), $"{TimeZoneInfo.ConvertTime(g.First().CapturedAt, zone):g} · {g.First().ForegroundApplication} · "
                    + string.Join(", ", (view.Evidence ?? []).Where(o => g.First().Path.Contains(o.CaptureId, StringComparison.Ordinal)).Select(o => o.Id)))).ToArray();
    }

    private static string Duration(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        + (seconds >= 86400 ? " + " + ((int)(seconds / 86400)).ToString(CultureInfo.InvariantCulture) + "d" : "");

    private void TimesheetSegments_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (TimesheetSegments.SelectedItem is SegmentPresentation selected) BeginTimesheetEdit(selected.Segment);
    }

    private void BeginTimesheetEdit(TimesheetSegment? segment)
    {
        if (_timesheetView is not { Selected: not null } view) return;
        _timesheetEditing = true; _timesheetEditRevision = view.Revision; _timesheetEditingSegment = segment;
        TimesheetEditor.Visibility = Visibility.Visible;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(view.TimeZoneId ?? "UTC");
        var start = segment?.Start ?? view.Coverage?.FirstObservation ?? view.Selected.CreatedAt;
        var end = segment?.End ?? start.AddMinutes(5);
        TimesheetEditStart.Text = TimeZoneInfo.ConvertTime(start, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        TimesheetEditEnd.Text = TimeZoneInfo.ConvertTime(end, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        TimesheetSplitAt.Text = TimeZoneInfo.ConvertTime(start.AddTicks((end - start).Ticks / 2), zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        TimesheetEditTask.Text = view.Tasks?.FirstOrDefault(t => t.Id == segment?.TaskId)?.Title ?? "";
        TimesheetEditProject.Text = segment?.Project ?? ""; TimesheetEditNote.Text = segment?.UserNote ?? "";
        TimesheetEditBasis.SelectedIndex = segment?.Basis is "unallocated" ? 1 : segment?.Basis is "unavailable" ? 2 : 0;
        TimesheetAlternatives.ItemsSource = new[] { T("Timesheet.CustomAttribution") }.Concat((segment?.Alternatives ?? [])
            .Select(a => (view.Tasks?.FirstOrDefault(t => t.Id == a.TaskId)?.Title ?? "") + " · " + a.Project)).ToArray();
        TimesheetAlternatives.SelectedIndex = 0;
    }

    private void TimesheetAdd_Click(object sender, RoutedEventArgs args) => BeginTimesheetEdit(null);

    private async Task SubmitTimesheetEditAsync(TimesheetBatchAction action)
    {
        if (_timesheetView?.Selected is not { } job) return;
        if (!DateTimeOffset.TryParseExact(TimesheetEditStart.Text, "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateTimeOffset.TryParseExact(TimesheetEditEnd.Text, "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        { ShowStatus("Timesheet.InvalidEdit", InfoBarSeverity.Error); return; }
        if (action == TimesheetBatchAction.Split && !DateTimeOffset.TryParseExact(TimesheetSplitAt.Text, "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        { ShowStatus("Timesheet.InvalidEdit", InfoBarSeverity.Error); return; }
        var next = _timesheetView.Segments?.FirstOrDefault(s => s.Start == _timesheetEditingSegment?.End);
        var alternative = TimesheetAlternatives.SelectedIndex > 0 ? _timesheetEditingSegment?.Alternatives?[TimesheetAlternatives.SelectedIndex - 1].Id : null;
        var edit = new TimesheetEdit(_timesheetEditingSegment?.Id, start, end, TimesheetEditTask.Text, TimesheetEditProject.Text,
            TimesheetEditBases[Math.Max(0, TimesheetEditBasis.SelectedIndex)], TimesheetEditNote.Text, next?.Id, alternative);
        var previousRevision = _timesheetEditRevision;
        await RunTimesheetAsync(new(action, JobId: job.Id, Page: _timesheetView.Page, Edit: edit,
            ExpectedRevision: _timesheetEditRevision, SegmentPage: _timesheetView.SegmentPage));
        if (_timesheetView?.Revision != previousRevision) ResetTimesheetReview();
    }

    private void ResetTimesheetReview()
    {
        _timesheetEditing = false; _timesheetEditingSegment = null;
        TimesheetEditor.Visibility = Visibility.Collapsed;
        TimesheetEvidenceList.ItemsSource = null;
        TimesheetSegments.SelectedItem = null;
    }

    private void TimesheetCancelEdit_Click(object sender, RoutedEventArgs args) => ResetTimesheetReview();

    private async void TimesheetSaveEdit_Click(object sender, RoutedEventArgs args) => await SubmitTimesheetEditAsync(TimesheetBatchAction.Edit);
    private async void TimesheetSplit_Click(object sender, RoutedEventArgs args) => await SubmitTimesheetEditAsync(TimesheetBatchAction.Split);
    private async void TimesheetMerge_Click(object sender, RoutedEventArgs args) => await SubmitTimesheetEditAsync(TimesheetBatchAction.Merge);
    private async void TimesheetRetry_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } job) await RunTimesheetAsync(new(TimesheetBatchAction.Retry, JobId: job.Id));
    }
    private async void TimesheetConfirm_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } job) await RunTimesheetAsync(new(TimesheetBatchAction.Confirm, JobId: job.Id, ExpectedRevision: _timesheetView.Revision));
    }
    private async void TimesheetEvidence_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } job)
            await RunTimesheetAsync(new(TimesheetBatchAction.Evidence, JobId: job.Id, SegmentId: _timesheetEditingSegment?.Id,
                Page: _timesheetView.Page, SegmentPage: _timesheetView.SegmentPage));
    }
    private void TimesheetEvidence_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not EvidencePresentation source) return;
        _timesheetEvidenceWindow?.Close();
        _timesheetEvidenceWindow = new ScreenshotWindow(_application, _messages, requestedScreenshotPath: source.Capture.Path,
            requestedCapturedAt: source.Capture.CapturedAt);
        _timesheetEvidenceWindow.Activate();
    }
    private async Task PageTimesheetSegmentsAsync(int delta)
    {
        if (_timesheetView?.Selected is not { } job) return;
        _timesheetEditing = false; TimesheetEditor.Visibility = Visibility.Collapsed; _timesheetEditingSegment = null;
        await RunTimesheetAsync(new(TimesheetBatchAction.List, JobId: job.Id, Page: _timesheetView.Page, SegmentPage: _timesheetView.SegmentPage + delta));
    }
    private async void TimesheetSegmentsPrevious_Click(object sender, RoutedEventArgs args) => await PageTimesheetSegmentsAsync(-1);
    private async void TimesheetSegmentsNext_Click(object sender, RoutedEventArgs args) => await PageTimesheetSegmentsAsync(1);

    private sealed record SegmentPresentation(TimesheetSegment Segment, string Heading, string Detail);
    private sealed record EvidencePresentation(ScreenshotGalleryItem Capture, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }
}
