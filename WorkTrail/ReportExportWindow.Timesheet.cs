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

internal sealed partial class ReportExportWindow
{
    private TimesheetBatchView? _timesheetView;
    private bool _applyingTimesheet;
    private bool _refreshingTimesheet;
    private DispatcherTimer? _timesheetTimer;

    private void UpdateTimesheetPanels()
    {
        if (TimesheetEmptyInfo is null) return;
        var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
        var hasJobs = _timesheetView is { } view
            && (view.Jobs.Count > 0 || view.HasPreviousPage || view.HasNextPage);
        TimesheetJobsPanel.Visibility = timesheet && hasJobs ? Visibility.Visible : Visibility.Collapsed;
        TimesheetEmptyInfo.Visibility = timesheet && _timesheetView is not null && !hasJobs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetTimesheetSelection()
    {
        if (_applyingTimesheet || _timesheetView is null) return;
        _timesheetView = _timesheetView with { Selected = null };
        _applyingTimesheet = true;
        TimesheetJobs.SelectedIndex = -1;
        _applyingTimesheet = false;
        TimesheetExportButton.IsEnabled = false;
        TimesheetCancelButton.IsEnabled = false;
        TimesheetStatus.Text = T("Timesheet.NewJob");
    }

    // sender and args indicate changed report choices, invalidating selection of a previous result.
    private void TimesheetOptionsChanged(object sender, RoutedEventArgs args) => ResetTimesheetSelection();

    // sender is the explicit day/week choice.
    // args identifies the selection change; a week has no morning/afternoon split.
    private void TimesheetGrouping_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (TimesheetMergeCheck is null) return;
        TimesheetMergeCheck.IsEnabled = TimesheetGroupingCombo.SelectedIndex != 1;
        ResetTimesheetSelection();
    }

    // sender identifies the explicit limited-preview command.
    // args describes the click that acknowledges the limit, generates the workbook and opens it.
    // Cancellation exceptions flow to RunAsync so the shared cancellation status remains consistent.
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

    // messageKey identifies the localized reason why preview generation or opening failed.
    // Dialog exceptions fall back to the standard inline export failure status.
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

    // sender identifies the grouping help link.
    // args describes the local explanation request; it never opens a website.
    private async void GroupingInfo_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            await _messages.ShowInformativeAsync(this,
                DialogRequest.Informative(T("Export.Grouping.Header"), T("Timesheet.GroupingInfo"), T("Dialog.Ok")));
        }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    private TimesheetOptions CollectTimesheetOptions() => new(
        new(CollectOptions() with { Format = ReportExportFormat.Excel },
            IncludeDescriptionExcerpt: false, IncludeCompleteDescription: TimesheetDescriptions.IsChecked == true,
            IncludeOcr: TimesheetOcr.IsChecked == true, IncludeWindowTitles: TimesheetTitles.IsChecked == true),
        TimesheetGroupingCombo.SelectedIndex == 1 || TimesheetMergeCheck.IsChecked == true, TimesheetConsultant.Text.Trim(), TimesheetClient.Text.Trim(),
        TimesheetBillingCheck.IsChecked == true ? (decimal)TimesheetRate.Value : null,
        TimesheetCurrency.Text.Trim().ToUpperInvariant(), TimesheetMonthsCheck.IsChecked == true,
        TimesheetGroupingCombo.SelectedIndex == 1 ? TimesheetGrouping.Week : TimesheetGrouping.Day);

    private async Task LoadTimesheetJobsAsync()
    {
        var result = await _application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.List), _lifetime.Token);
        if (_closed) return;
        if (result.Succeeded && result.Value is { } view) RenderTimesheet(view);
        else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
        _timesheetTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timesheetTimer.Tick += TimesheetTimer_Tick;
        Closed += (_, _) => _timesheetTimer.Stop();
        _timesheetTimer.Start();
    }

    // sender is the periodic UI timer; the runtime independently retrieves cloud results.
    // args describes the tick, which must not disable controls or disturb a newer selection.
    private async void TimesheetTimer_Tick(object? sender, object args)
    {
        var previous = _timesheetView;
        if (_busy || _closed || _refreshingTimesheet || TimesheetJobs.IsDropDownOpen
            || !ReferenceEquals(Navigation.SelectedItem, TimesheetTab)
            || previous?.Selected is not { } selected) return;

        _refreshingTimesheet = true;
        try
        {
            var result = await _application.ManageTimesheetBatchAsync(
                new(TimesheetBatchAction.List, JobId: selected.Id, Page: previous.Page), _lifetime.Token);
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

    // command contains user choices; the facade owns persistence, credentials and networking.
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

    // view contains only bounded UI rows and job metadata; the complete data stays in Core.
    private void RenderTimesheet(TimesheetBatchView view)
    {
        _timesheetView = view;
        UpdateTimesheetPanels();
        _applyingTimesheet = true;
        var jobLabels = view.Jobs.Select(job => $"{job.From:d} — {job.ToInclusive:d} · {job.CreatedAt.ToLocalTime():g} · {T("Timesheet.State." + job.State)}").ToArray();
        if (TimesheetJobs.ItemsSource is not IEnumerable<string> previousLabels || !previousLabels.SequenceEqual(jobLabels))
            TimesheetJobs.ItemsSource = jobLabels;
        TimesheetJobs.SelectedIndex = view.Selected is { } selected ? view.Jobs.ToList().FindIndex(job => job.Id == selected.Id) : -1;
        _applyingTimesheet = false;
        var current = view.Selected;
        var terminal = current?.State is "completed" or "failed" or "expired" or "cancelled";
        TimesheetExportButton.IsEnabled = current?.ResultsSaved == true;
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

    // sender and args identify the explicit paid batch submission.
    private async void TimesheetStart_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetOptionsAsync(TimesheetBatchAction.Start);

    // action selects the explicit submission; invalid numeric input is reported instead of escaping async void.
    private async Task RunTimesheetOptionsAsync(TimesheetBatchAction action)
    {
        try { await RunTimesheetAsync(new(action, CollectTimesheetOptions())); }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    // sender and args identify an explicit status refresh, including recovery after a lost create response.
    private async void TimesheetRefresh_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetAsync(new(_timesheetView?.Selected is not null ? TimesheetBatchAction.Refresh : TimesheetBatchAction.List,
            JobId: _timesheetView?.Selected?.Id, Page: _timesheetView?.Page ?? 0));

    // sender and args identify explicit cloud cancellation; closing the window does not use this action.
    private async void TimesheetCancel_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } selected)
            await RunTimesheetAsync(new(TimesheetBatchAction.Cancel, JobId: selected.Id, Page: _timesheetView!.Page));
    }

    // sender and args identify the selected durable job; changing jobs does not generate summaries.
    private async void TimesheetJobs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_applyingTimesheet || _busy || _timesheetView is null || TimesheetJobs.SelectedIndex < 0) return;
        await RunTimesheetAsync(new(TimesheetBatchAction.List, JobId: _timesheetView.Jobs[TimesheetJobs.SelectedIndex].Id, Page: _timesheetView.Page));
    }

    // sender and args identify the explicit destination picker for the saved result snapshot.
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
    // sender and args identify an explicit archive-page change; no cloud request is submitted.
    private async void TimesheetPrevious_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView is { HasPreviousPage: true } view)
            await RunTimesheetAsync(new(TimesheetBatchAction.List, Page: view.Page - 1));
    }

    // sender and args identify the next bounded page of saved jobs.
    private async void TimesheetNext_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView is { HasNextPage: true } view)
            await RunTimesheetAsync(new(TimesheetBatchAction.List, Page: view.Page + 1));
    }

}
