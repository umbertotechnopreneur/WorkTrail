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
using WorkTrail.Application;
using Windows.Storage.Pickers;

namespace WorkTrail;

/// <summary>
/// Coordinates the report-export window's timesheet batch controls while delegating all work to the application boundary.
/// </summary>
internal sealed partial class ReportExportWindow
{
    private TimesheetBatchView? _timesheetView;
    private bool _applyingTimesheet;
    private bool _refreshingTimesheet;
    private DispatcherTimer? _timesheetTimer;

    /// <summary>
    /// Shows the populated or empty timesheet state for the currently selected tab.
    /// </summary>
    private void UpdateTimesheetPanels()
    {
        if (TimesheetEmptyInfo is null) return;
        var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
        var hasJobs = _timesheetView is { } view
            && (view.Jobs.Count > 0 || view.HasPreviousPage || view.HasNextPage);
        TimesheetJobsPanel.Visibility = timesheet && hasJobs ? Visibility.Visible : Visibility.Collapsed;
        TimesheetEmptyInfo.Visibility = timesheet && _timesheetView is not null && !hasJobs ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Clears the selected job after an option change without triggering a new application request.
    /// </summary>
    private void ResetTimesheetSelection()
    {
        if (_applyingTimesheet || _timesheetView is null) return;
        ResetTimesheetReview();
        _timesheetView = _timesheetView with { Selected = null };
        _applyingTimesheet = true;
        TimesheetJobs.SelectedIndex = -1;
        _applyingTimesheet = false;
        TimesheetExportButton.IsEnabled = false;
        TimesheetCancelButton.IsEnabled = false;
        TimesheetStatus.Text = T("Timesheet.NewJob");
    }

    /// <summary>
    /// Clears the previous job selection when report options change.
    /// </summary>
    private void TimesheetOptionsChanged(object sender, RoutedEventArgs args) => ResetTimesheetSelection();

    /// <summary>
    /// Updates merge availability and clears the previous job selection when the grouping changes.
    /// </summary>
    private void TimesheetGrouping_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        ResetTimesheetSelection();
    }

    /// <summary>
    /// Generates and opens a limited local preview through the application boundary.
    /// </summary>
    private async void ExcelPreview_Click(object sender, RoutedEventArgs args) => await RunAsync(async token =>
    {
        try
        {
            await _messages.ShowInformativeAsync(this,
                DialogRequest.Informative(T("Timesheet.PreviewLimit.Title"), T("Timesheet.PreviewLimit.Message"), T("Dialog.Ok")));
            if (_closed) return;
            token.ThrowIfCancellationRequested();

            var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
            var id = timesheet && _timesheetView?.Selected is { } selected ? selected.Id : (Guid?)null;
            var generated = await _application.OpenReportFilePreviewAsync(new(CollectOptions(),
                IncludeSummaryCheck.IsChecked == true ? _summary : null, timesheet && id is null ? CollectTimesheetOptions() : null, id), token);
            if (_closed) return;
            if (!generated.Succeeded || generated.Value is null)
            {
                await ShowPreviewFailureAsync(generated.MessageKey);
                return;
            }

            var opened = await _application.OpenReportFilePreviewAsync(
                new(CollectOptions(), Action: ReportFilePreviewAction.Open, PreviewPath: generated.Value.Path), token);
            if (_closed) return;
            if (!opened.Succeeded)
            {
                await ShowPreviewFailureAsync(opened.MessageKey);
                return;
            }

            ShowStatus("Timesheet.PreviewOpened", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (!_closed) await ShowPreviewFailureAsync("Export.Failed");
        }
    });

    /// <summary>
    /// Displays a localized preview failure and falls back to the inline export status if the dialog fails.
    /// </summary>
    private async Task ShowPreviewFailureAsync(string messageKey)
    {
        try
        {
            await _messages.ShowInformativeAsync(this,
                DialogRequest.Informative(T("Export.Title"), T(messageKey), T("Dialog.Ok")));
        }
        catch (Exception)
        {
            ShowStatus("Export.Failed", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Displays the local explanation of timesheet grouping choices.
    /// </summary>
    private async void GroupingInfo_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            await _messages.ShowInformativeAsync(this,
                DialogRequest.Informative(T("Export.Grouping.Header"), T("Timesheet.GroupingInfo"), T("Dialog.Ok")));
        }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    /// <summary>
    /// Collects the current UI values into a timesheet request without performing export or network work.
    /// </summary>
    private TimesheetOptions CollectTimesheetOptions()
    {
        var reportOptions = CollectOptions() with { Format = ReportExportFormat.Excel };
        var isWeeklyGrouping = TimesheetGroupingCombo.SelectedIndex == 1;
        var consultant = TimesheetConsultant.Text.Trim();
        var client = TimesheetClient.Text.Trim();
        var currency = TimesheetCurrency.Text.Trim().ToUpperInvariant();
        var grouping = isWeeklyGrouping ? TimesheetGrouping.Week : TimesheetGrouping.Day;

        return new(
            new(
                reportOptions,
                IncludeDescriptionExcerpt: false,
                IncludeCompleteDescription: TimesheetDescriptions.IsChecked == true,
                IncludeWindowTitles: TimesheetTitles.IsChecked == true),
            consultant,
            client,
            TimesheetBillingCheck.IsChecked == true ? (decimal)TimesheetRate.Value : null,
            currency,
            TimesheetMonthsCheck.IsChecked == true,
            grouping);
    }

    /// <summary>
    /// Loads the current timesheet jobs and starts the UI-only refresh timer.
    /// </summary>
    private async Task LoadTimesheetJobsAsync()
    {
        InitializeTimesheetReview();
        var result = await _application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.List), _lifetime.Token);
        if (_closed) return;
        if (result.Succeeded && result.Value is { } view) RenderTimesheet(view);
        else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
        _timesheetTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timesheetTimer.Tick += TimesheetTimer_Tick;
        Closed += (_, _) => _timesheetTimer.Stop();
        _timesheetTimer.Start();
    }

    /// <summary>
    /// Refreshes the selected job on the periodic UI timer without overriding newer user interaction.
    /// </summary>
    private async void TimesheetTimer_Tick(object? sender, object args)
    {
        var previous = _timesheetView;
        if (_busy || _closed || _refreshingTimesheet || _timesheetEditing || TimesheetJobs.IsDropDownOpen
            || !ReferenceEquals(Navigation.SelectedItem, TimesheetTab)
            || previous?.Selected is not { } selected) return;

        _refreshingTimesheet = true;
        try
        {
            var result = await _application.ManageTimesheetBatchAsync(
                new(TimesheetBatchAction.List, JobId: selected.Id, Page: previous.Page, SegmentPage: previous.SegmentPage), _lifetime.Token);
            // A user action wins over an older poll, even if both complete on the UI thread.
            if (_closed || _busy || TimesheetJobs.IsDropDownOpen || !ReferenceEquals(_timesheetView, previous)
                || !ReferenceEquals(Navigation.SelectedItem, TimesheetTab)) return;
            if (result.Succeeded && result.Value is { } view)
            {
                RenderTimesheet(view);
                if (view.WarningKey is { } warning) ShowStatus(warning, InfoBarSeverity.Warning);
            }
            else ShowStatus(result.MessageKey, InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Warning); }
        finally { _refreshingTimesheet = false; }
    }

    /// <summary>
    /// Sends a timesheet command to the application boundary and renders its bounded result.
    /// </summary>
    private async Task RunTimesheetAsync(TimesheetBatchCommand command) => await RunAsync(async token =>
    {
        var result = await _application.ManageTimesheetBatchAsync(command, token);
        if (result.Succeeded && result.Value is { } view)
        {
            RenderTimesheet(view);
            if (view.WarningKey is { } warning) ShowStatus(warning, InfoBarSeverity.Warning);
            if (view.ExportedPath is { } path)
            {
                StatusBar.Message = _strings.Format("Export.SavedTo", path);
                StatusBar.Severity = InfoBarSeverity.Success; StatusBar.IsOpen = true;
            }
        }
        else if (result.MessageKey == "Premium.Required") await ShowUpgradeAsync();
        else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
    });

    /// <summary>
    /// Renders the bounded timesheet job view returned by the application boundary.
    /// </summary>
    private void RenderTimesheet(TimesheetBatchView view)
    {
        if (_timesheetView?.Selected?.Id != view.Selected?.Id) ResetTimesheetReview();
        _timesheetView = view;
        RenderTimesheetReview(view);
        UpdateTimesheetPanels();
        _applyingTimesheet = true;
        var jobLabels = view.Jobs.Select(job => $"{job.From:d} — {job.ToInclusive:d} · {job.CreatedAt.ToLocalTime():g} · {T("Timesheet.State." + job.State)}").ToArray();
        if (TimesheetJobs.ItemsSource is not IEnumerable<string> previousLabels || !previousLabels.SequenceEqual(jobLabels))
            TimesheetJobs.ItemsSource = jobLabels;
        TimesheetJobs.SelectedIndex = view.Selected is { } selected ? view.Jobs.ToList().FindIndex(job => job.Id == selected.Id) : -1;
        _applyingTimesheet = false;
        var current = view.Selected;
        var terminal = current?.State is "completed" or "failed" or "expired" or "cancelled" or "partial" or "historical";
        TimesheetExportButton.IsEnabled = current?.ResultsSaved == true && !view.Historical;
        TimesheetCancelButton.IsEnabled = current is not null && !terminal && current.State != "prepared";
        TimesheetPreviousButton.IsEnabled = view.HasPreviousPage;
        TimesheetNextButton.IsEnabled = view.HasNextPage;
        TimesheetPaginationPanel.Visibility = view.HasPreviousPage || view.HasNextPage ? Visibility.Visible : Visibility.Collapsed;
        TimesheetStatus.Text = current is null ? T("Timesheet.NewJob")
            : _strings.Format("Timesheet.Progress", T("Timesheet.State." + current.State), current.CompletedCount, current.RowCount, current.FailedCount)
                + $"\n{current.From:d} — {current.ToInclusive:d}"
                + (terminal && !current.ResultsSaved ? "\n" + T("Timesheet.ResultsPending") : "")
                + (current.ResultsSaved && current.CleanupPending ? "\n" + T("Timesheet.CleanupPending") : "");
    }

    /// <summary>
    /// Starts the explicitly requested paid timesheet batch.
    /// </summary>
    private async void TimesheetStart_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetOptionsAsync(TimesheetBatchAction.Start);

    /// <summary>
    /// Submits the selected batch action and converts UI input failures to visible status.
    /// </summary>
    private async Task RunTimesheetOptionsAsync(TimesheetBatchAction action)
    {
        try { await RunTimesheetAsync(new(action, CollectTimesheetOptions())); }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    /// <summary>
    /// Refreshes the selected job or the current jobs page after an explicit user request.
    /// </summary>
    private async void TimesheetRefresh_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetAsync(new(_timesheetView?.Selected is not null ? TimesheetBatchAction.Refresh : TimesheetBatchAction.List,
            JobId: _timesheetView?.Selected?.Id, Page: _timesheetView?.Page ?? 0, SegmentPage: _timesheetView?.SegmentPage ?? 0));

    /// <summary>
    /// Requests cancellation only for the currently selected durable batch job.
    /// </summary>
    private async void TimesheetCancel_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } selected)
            await RunTimesheetAsync(new(TimesheetBatchAction.Cancel, JobId: selected.Id, Page: _timesheetView!.Page));
    }

    /// <summary>
    /// Loads the explicitly selected persisted job without generating a new timesheet.
    /// </summary>
    private async void TimesheetJobs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_applyingTimesheet || _busy || _timesheetView is null || TimesheetJobs.SelectedIndex < 0) return;
        await RunTimesheetAsync(new(TimesheetBatchAction.List, JobId: _timesheetView.Jobs[TimesheetJobs.SelectedIndex].Id, Page: _timesheetView.Page));
    }

    /// <summary>
    /// Lets the user choose a destination and exports the selected saved result snapshot.
    /// </summary>
    private async void TimesheetExport_Click(object sender, RoutedEventArgs args) => await RunAsync(async token =>
    {
        if (_timesheetView?.Selected is not { } selected) return;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"WorkTrail_Timesheet_{selected.From:yyyy-MM-dd}_{selected.ToInclusive:yyyy-MM-dd}"
        };
        picker.FileTypeChoices.Add(T("Timesheet.Title"), [".xlsx"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        var destination = await picker.PickSaveFileAsync();
        if (destination is null || token.IsCancellationRequested) return;
        var result = await _application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.Export, JobId: selected.Id,
            DestinationPath: destination.Path, Overwrite: true, Page: _timesheetView!.Page, Theme: CollectOptions().Theme), token);
        if (result.Succeeded && result.Value is { } view)
        {
            RenderTimesheet(view);
            StatusBar.Message = _strings.Format("Export.SavedTo", view.ExportedPath);
            StatusBar.Severity = InfoBarSeverity.Success; StatusBar.IsOpen = true;
        }
        else if (result.MessageKey == "Premium.Required") await ShowUpgradeAsync();
        else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
    });
    /// <summary>
    /// Loads the preceding bounded page of saved timesheet jobs.
    /// </summary>
    private async void TimesheetPrevious_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView is { HasPreviousPage: true } view)
            await RunTimesheetAsync(new(TimesheetBatchAction.List, Page: view.Page - 1));
    }

    /// <summary>
    /// Loads the next bounded page of saved timesheet jobs.
    /// </summary>
    private async void TimesheetNext_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView is { HasNextPage: true } view)
            await RunTimesheetAsync(new(TimesheetBatchAction.List, Page: view.Page + 1));
    }

}
