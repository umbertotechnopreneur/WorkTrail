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
    private bool _refreshingTimesheet;
    private DispatcherTimer? _timesheetTimer;
    private string? _excelPreviewPath;

    private void UpdateTimesheetPanels()
    {
        if (TimesheetEmptyInfo is null) return;
        var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
        var hasJobs = _timesheetView is { } view
            && (view.Jobs.Count > 0 || view.HasPreviousPage || view.HasNextPage);
        TimesheetJobsPanel.Visibility = timesheet && hasJobs ? Visibility.Visible : Visibility.Collapsed;
        TimesheetEmptyInfo.Visibility = timesheet && _timesheetView is not null && !hasJobs ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetExcelPreview()
    {
        if (ExcelPreviewStatusPanel is null) return;
        _excelPreviewPath = null;
        ExcelPreviewPath.Text = "";
        ExcelPreviewFilePanel.Visibility = Visibility.Collapsed;
        ExcelPreviewStatusPanel.Visibility = Visibility.Collapsed;
    }

    private void ResetTimesheetSelection()
    {
        ResetExcelPreview();
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

    // sender identifies the explicit generate button.
    // args describes the click; Excel opens only after a separate request.
    private async void ExcelPreview_Click(object sender, RoutedEventArgs args) => await RunAsync(async token =>
    {
        ResetExcelPreview();
        ExcelPreviewStatusPanel.Visibility = Visibility.Visible;
        ExcelPreviewStatus.Text = T("Timesheet.PreviewGenerating");
        ExcelPreviewProgress.Visibility = Visibility.Visible;
        try
        {
            var timesheet = ReferenceEquals(Navigation.SelectedItem, TimesheetTab);
            var id = timesheet && _timesheetView?.Selected is { } selected ? selected.Id : (Guid?)null;
            var result = await _application.OpenReportFilePreviewAsync(new(CollectOptions(),
                IncludeSummaryCheck.IsChecked == true ? _summary : null, timesheet && id is null ? CollectTimesheetOptions() : null, id), token);
            if (_closed) return;
            if (!result.Succeeded || result.Value is null)
            {
                ExcelPreviewStatus.Text = T(result.MessageKey);
                ShowStatus(result.MessageKey, InfoBarSeverity.Error);
                return;
            }
            _excelPreviewPath = result.Value.Path;
            ExcelPreviewPath.Text = _excelPreviewPath;
            ExcelPreviewStatus.Text = T("Timesheet.PreviewReady");
            ExcelPreviewFilePanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_closed) ExcelPreviewStatus.Text = T("Export.Cancelled");
            throw;
        }
        catch (Exception)
        {
            if (!_closed) ExcelPreviewStatus.Text = T("Export.Failed");
            throw;
        }
        finally { if (!_closed) ExcelPreviewProgress.Visibility = Visibility.Collapsed; }
    });

    // sender identifies a report option that changes the generated workbook.
    // args describes the click; the existing file is hidden until regenerated with current choices.
    private void ExcelPreviewOptionsChanged(object sender, RoutedEventArgs args) => ResetExcelPreview();

    // sender identifies the open command for the completed workbook.
    // args describes the click; this action reuses the file and never regenerates data.
    private async void ExcelPreviewOpen_Click(object sender, RoutedEventArgs args) =>
        await RunExcelPreviewActionAsync(ReportFilePreviewAction.Open);

    // sender identifies the clipboard icon for the completed workbook.
    // args describes the click; the runtime copies only its absolute path.
    private async void ExcelPreviewCopy_Click(object sender, RoutedEventArgs args) =>
        await RunExcelPreviewActionAsync(ReportFilePreviewAction.CopyPath);

    // action selects opening the existing file or copying its path, without creating another preview.
    private Task RunExcelPreviewActionAsync(ReportFilePreviewAction action) => RunAsync(async token =>
    {
        if (_excelPreviewPath is not { } path) return;
        var result = await _application.OpenReportFilePreviewAsync(new(CollectOptions(), Action: action, PreviewPath: path), token);
        if (_closed) return;
        var message = result.Succeeded
            ? action == ReportFilePreviewAction.CopyPath ? "Timesheet.PreviewPathCopied" : "Timesheet.PreviewOpened"
            : result.MessageKey;
        ExcelPreviewStatus.Text = T(message);
        ShowStatus(message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    });

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
        if (ReferenceEquals(Navigation.SelectedItem, TimesheetTab)
            && (_timesheetView?.Selected?.Id != view.Selected?.Id
                || _timesheetView?.Selected?.ResultsSaved != view.Selected?.ResultsSaved)) ResetExcelPreview();
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
            DestinationPath: destination.Path, Overwrite: true, Page: _timesheetView!.Page), token);
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
