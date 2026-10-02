// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using Windows.Storage.Pickers;

namespace WorkTrail;

internal sealed partial class ReportExportWindow
{
    private TimesheetBatchView? _timesheetView;
    private bool _applyingTimesheet;
    private DispatcherTimer? _timesheetTimer;

    private void ResetTimesheetSelection()
    {
        if (_applyingTimesheet || _timesheetView is null) return;
        _timesheetView = _timesheetView with { Selected = null, Rows = [] };
        _applyingTimesheet = true;
        TimesheetJobs.SelectedIndex = -1;
        _applyingTimesheet = false;
        TimesheetExportButton.IsEnabled = false;
        TimesheetCancelButton.IsEnabled = false;
        TimesheetStatus.Text = T("Timesheet.NewJob");
    }

    // sender and args indicate changed report choices, invalidating selection of a previous result.
    private void TimesheetOptionsChanged(object sender, RoutedEventArgs args) => ResetTimesheetSelection();

    // sender and args identify the local, bounded workbook preview.
    private async void ExcelPreview_Click(object sender, RoutedEventArgs args) => await RunAsync(async token =>
    {
        var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
        var id = timesheet && _timesheetView?.Selected is { State: not "preview" } selected ? selected.Id : (Guid?)null;
        var result = await _application.OpenReportFilePreviewAsync(new(CollectOptions(),
            IncludeSummaryCheck.IsChecked == true ? _summary : null, timesheet && id is null ? CollectTimesheetOptions() : null, id), token);
        ShowStatus(result.Succeeded ? "Timesheet.PreviewOpened" : result.MessageKey,
            result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    });

    // sender and args identify the local grouping explanation; it never opens a website.
    private async void GroupingInfo_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            await _messages.ShowInformativeAsync(this,
                DialogRequest.Informative(T("Export.Grouping"), T("Timesheet.GroupingInfo"), T("Dialog.Ok")));
        }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    private TimesheetOptions CollectTimesheetOptions() => new(
        new(CollectOptions() with { Format = ReportExportFormat.Excel, IncludeAiUsage = false },
            IncludeDescriptionExcerpt: false, IncludeCompleteDescription: TimesheetDescriptions.IsChecked == true,
            IncludeOcr: TimesheetOcr.IsChecked == true, IncludeWindowTitles: TimesheetTitles.IsChecked == true),
        TimesheetMergeCheck.IsChecked == true, TimesheetConsultant.Text.Trim(), TimesheetClient.Text.Trim(),
        TimesheetBillingCheck.IsChecked == true ? (decimal)TimesheetRate.Value : null,
        TimesheetCurrency.Text.Trim().ToUpperInvariant(), TimesheetMonthsCheck.IsChecked == true);

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

    // sender and args identify a passive poll; new paid work is never scheduled here.
    private async void TimesheetTimer_Tick(object? sender, object args)
    {
        if (_busy || _closed || !ReferenceEquals(Navigation.SelectedItem, TimesheetTab)
            || _timesheetView?.Selected is not { State: "validating" or "in_progress" or "finalizing" or "cancelling" } selected) return;
        await RunTimesheetAsync(new(TimesheetBatchAction.Refresh, JobId: selected.Id));
    }

    // command contains user choices; the facade owns persistence, credentials and networking.
    private async Task RunTimesheetAsync(TimesheetBatchCommand command) => await RunAsync(async token =>
    {
        var result = await _application.ManageTimesheetBatchAsync(command, token);
        if (result.Succeeded && result.Value is { } view)
        {
            RenderTimesheet(view);
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
        _applyingTimesheet = true;
        TimesheetJobs.ItemsSource = view.Jobs.Select(job => $"{job.From:d} — {job.ToInclusive:d} · {job.CreatedAt.ToLocalTime():g} · {T("Timesheet.State." + job.State)}").ToArray();
        TimesheetJobs.SelectedIndex = view.Selected is { } selected ? view.Jobs.ToList().FindIndex(job => job.Id == selected.Id) : -1;
        _applyingTimesheet = false;
        var current = view.Selected;
        var terminal = current?.State is "completed" or "failed" or "expired" or "cancelled";
        TimesheetExportButton.IsEnabled = terminal;
        TimesheetCancelButton.IsEnabled = current is not null && !terminal && current.State is not ("preview" or "prepared");
        TimesheetStatus.Text = current is null ? T("Timesheet.Empty")
            : _strings.Format("Timesheet.Progress", T("Timesheet.State." + current.State), current.CompletedCount, current.RowCount, current.FailedCount)
                + $"\n{current.From:d} — {current.ToInclusive:d}";
    }

    // sender and args identify the explicit paid batch submission.
    private async void TimesheetStart_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetOptionsAsync(TimesheetBatchAction.Start);

    // action is Preview or Start; collecting invalid numeric input is reported instead of escaping async void.
    private async Task RunTimesheetOptionsAsync(TimesheetBatchAction action)
    {
        try { await RunTimesheetAsync(new(action, CollectTimesheetOptions())); }
        catch (Exception) { ShowStatus("Export.Failed", InfoBarSeverity.Error); }
    }

    // sender and args identify an explicit status refresh, including recovery after a lost create response.
    private async void TimesheetRefresh_Click(object sender, RoutedEventArgs args) =>
        await RunTimesheetAsync(new(_timesheetView?.Selected is { State: not "preview" } ? TimesheetBatchAction.Refresh : TimesheetBatchAction.List,
            JobId: _timesheetView?.Selected is { State: not "preview" } selected ? selected.Id : null));

    // sender and args identify explicit cloud cancellation; closing the window does not use this action.
    private async void TimesheetCancel_Click(object sender, RoutedEventArgs args)
    {
        if (_timesheetView?.Selected is { } selected)
            await RunTimesheetAsync(new(TimesheetBatchAction.Cancel, JobId: selected.Id));
    }

    // sender and args identify the selected durable job; changing jobs does not generate summaries.
    private async void TimesheetJobs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_applyingTimesheet || _busy || _timesheetView is null || TimesheetJobs.SelectedIndex < 0) return;
        await RunTimesheetAsync(new(TimesheetBatchAction.List, JobId: _timesheetView.Jobs[TimesheetJobs.SelectedIndex].Id));
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
        if (destination is null) return;
        var result = await _application.ManageTimesheetBatchAsync(new(TimesheetBatchAction.Export, JobId: selected.Id,
            DestinationPath: destination.Path, Overwrite: true), token);
        if (result.Succeeded && result.Value is { } view)
        {
            RenderTimesheet(view);
            StatusBar.Message = _strings.Format("Export.SavedTo", view.ExportedPath);
            StatusBar.Severity = InfoBarSeverity.Success; StatusBar.IsOpen = true;
        }
        else if (result.MessageKey == "Premium.Required") await ShowUpgradeAsync();
        else ShowStatus(result.MessageKey, InfoBarSeverity.Error);
    });
}
