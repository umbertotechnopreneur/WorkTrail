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


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects and renders data-retention preview and cleanup operations.</summary>
public sealed partial class RetentionOperationsControl : UserControl
{
    private LocalizationService _strings = new("system");
    private OperationsSectionContext? _context;
    private bool _confirmationOpen;
    private bool _loadingPolicy;
    private bool _policyLoadFailed;
    private RetentionStatus? _retentionStatus;
    private RetentionPreview? _retentionPreview;
    private bool _retentionExecuted;
    private bool _renderingStorageSettings;
    private Task _storageSaveQueue = Task.CompletedTask;
    private int _pendingStorageSaves;

    /// <summary>Creates the independent retention operations surface.</summary>
    public RetentionOperationsControl()
    {
        InitializeComponent();
        StorageWarningBox.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 2 };
    }

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        CaptureStorageExpander.Header = _strings.Translate("History.CaptureSettings");
        RetentionMonthsBox.Header = _strings.Translate("Operations.Retention.PolicyMonths");
        StorageWarningBox.Header = _strings.Translate("History.WarningThreshold");
        DeleteFromPicker.Header = _strings.Translate("History.From");
        DeleteToPicker.Header = _strings.Translate("History.To");
        DeleteFromPicker.PlaceholderText = DeleteToPicker.PlaceholderText = _strings.Translate("History.SelectDate");
        CaptureSettingsExpander.Header = _strings.Translate("Options.Section.Snapshots");
        OcrSettingsExpander.Header = _strings.Translate("Options.Ocr.Section");
        TierBadge.Text = _strings.Translate("Premium.Badge");
        UiLocalization.SetAccessibleLabel(OpenFolderButton, _strings.Translate("Options.OpenFolderAction"));
        AutomationProperties.SetName(KeepScreenshotsSwitch, _strings.Translate("Options.KeepSnapshots.Header"));
        AutomationProperties.SetName(ScreenshotsEnabledSwitch, _strings.Translate("Options.SnapshotsEnabled.Header"));
        AutomationProperties.SetName(OcrEnabledSwitch, _strings.Translate("Options.Ocr.Enabled"));
        _renderingStorageSettings = true;
        try
        {
            RetentionMonthsBox.Items.Clear();
            for (var months = 1; months <= 12; months++)
                RetentionMonthsBox.Items.Add(new ComboBoxItem
                {
                    Tag = months,
                    Content = months == 1 ? _strings.Translate("Operations.Retention.OneMonth") : _strings.Format("History.Months", months)
                });
            OcrLanguageBox.Items.Clear();
            foreach (var ocrLanguage in SettingsCatalog.Definitions.Single(setting => setting.Key == "ocr.language").AllowedValues)
            {
                // Language labels use translated captions where available and the culture's native name otherwise.
                var label = _strings.TryTranslate($"Options.Ocr.Language.{ocrLanguage}", out var translated)
                    ? translated : CultureInfo.GetCultureInfo(ocrLanguage).NativeName;
                OcrLanguageBox.Items.Add(new ComboBoxItem { Tag = ocrLanguage, Content = label });
            }
        }
        finally { _renderingStorageSettings = false; }
        AutomationProperties.SetName(RetentionMonthsBox, _strings.Translate("Operations.Retention.PolicyMonths"));
        AutomationProperties.SetName(StorageWarningBox, _strings.Translate("History.WarningThreshold"));
        AutomationProperties.SetName(DeleteFromPicker, _strings.Translate("History.From"));
        AutomationProperties.SetName(DeleteToPicker, _strings.Translate("History.To"));
        RenderRetentionStatus();
        RenderRetentionPreviewState();
    }

    internal void Initialize(IWorkTrailApplication application, MicaDialogService dialogs, Window ownerWindow, TimedInfoBar banner) =>
        _context = new OperationsSectionContext(
            application,
            dialogs,
            ownerWindow,
            banner,
            active =>
            {
                Progress.IsActive = active;
                Progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            },
            SectionBody,
            key => _strings.TryTranslate(key, out var value) ? value : null);

    private OperationsSectionContext Context => _context ?? throw new InvalidOperationException("RetentionOperationsControl must be initialized before use.");

    /// <summary>Loads current retention criteria for the visible page without starting preview or cleanup.</summary>
    internal async Task LoadAsync()
    {
        await _storageSaveQueue;
        if (_loadingPolicy)
        {
            // Reopening the page during the same request must not start a competing load.
            return;
        }

        _loadingPolicy = true;
        try
        {
            _policyLoadFailed = false;
            _retentionStatus = null;
            _retentionPreview = null;
            _retentionExecuted = false;
            RenderRetentionStatus();
            RenderRetentionPreviewState();

            var result = await Context.ExecuteAsync(
                (application, token) => application.GetRetentionStatusAsync(token),
                showSuccess: false);
            if (result is not { Succeeded: true, Value: { } status })
            {
                // Failed loading leaves no stale policy or enabled cleanup action on screen.
                _policyLoadFailed = true;
                return;
            }

            _retentionStatus = status;
        }
        finally
        {
            _loadingPolicy = false;
            RenderRetentionStatus();
        }
    }

    private void RenderRetentionStatus()
    {
        var ready = !_loadingPolicy && !_confirmationOpen && _retentionStatus is not null;
        RetentionCleanupButton.IsEnabled = ready && SelectedRange() is { IsValid: true };
        DeleteFromPicker.IsEnabled = DeleteToPicker.IsEnabled = ready;
        OpenFolderButton.IsEnabled = RetentionMonthsBox.IsEnabled = StorageWarningBox.IsEnabled = ready;
        ScreenshotFolderBox.IsEnabled = KeepScreenshotsSwitch.IsEnabled = ready;
        ScreenshotsEnabledSwitch.IsEnabled = ScreenshotModeBox.IsEnabled = OcrEnabledSwitch.IsEnabled = ready;
        OcrLanguageBox.IsEnabled = ready && _retentionStatus?.OcrEnabled == true;
        HardwareSaveSnapshotsSwitch.IsEnabled = ready && _retentionStatus?.HardwareSensorsEnabled == true;
        TierBadge.Visibility = _retentionStatus?.MaximumMonths == 12 ? Visibility.Visible : Visibility.Collapsed;
        StorageWarning.IsOpen = _retentionStatus?.IsStorageWarning == true;
        StorageWarning.Title = _strings.Translate("History.WarningTitle");
        StorageWarning.Message = _retentionStatus is { } storage
            ? _strings.Format("History.WarningMessage", FormatBytes(storage.StorageBytes), FormatBytes(storage.HistoryWarningMegabytes * 1024L * 1024L)) : string.Empty;
        StorageUsageText.Text = _retentionStatus is { } usage
            ? _strings.Format("History.Usage", FormatBytes(usage.StorageBytes)) : string.Empty;
        if (_retentionStatus is { } status)
        {
            RetentionStatusText.Text = _strings.Format(
                "Operations.Retention.StatusMonths",
                status.DataRetentionDays / 30,
                status.ScreenshotRetentionDays / 30);
            _renderingStorageSettings = true;
            try
            {
                RetentionMonthsBox.SelectedItem = RetentionMonthsBox.Items.OfType<ComboBoxItem>()
                    .SingleOrDefault(item => item.Tag is int months && months == status.DataRetentionDays / 30);
                StorageWarningBox.Value = status.HistoryWarningMegabytes / 1024d;
                ScreenshotFolderBox.Text = status.ScreenshotDirectory;
                KeepScreenshotsSwitch.IsOn = status.KeepScreenshots;
                ScreenshotsEnabledSwitch.IsOn = status.ScreenshotsEnabled;
                ScreenshotModeBox.SelectedItem = ScreenshotModeBox.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), status.ScreenshotCaptureMode, StringComparison.Ordinal));
                ScreenshotModeHintBox.Text = _strings.Translate(status.ScreenshotCaptureMode == "active-window" ? "Options.SnapshotHintActive" : "Options.SnapshotHintAll");
                OcrEnabledSwitch.IsOn = status.OcrEnabled;
                OcrLanguageBox.SelectedItem = OcrLanguageBox.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), status.OcrLanguage, StringComparison.Ordinal));
                HardwareSaveSnapshotsSwitch.IsOn = status.HardwareSaveSnapshots;
            }
            finally { _renderingStorageSettings = false; }
            return;
        }

        RetentionStatusText.Text = _strings.Translate(_policyLoadFailed
            ? "Operations.Retention.Unavailable"
            : "Operations.Initial.Retention");
        _renderingStorageSettings = true;
        try { ScreenshotFolderBox.Text = string.Empty; KeepScreenshotsSwitch.IsOn = false; }
        finally { _renderingStorageSettings = false; }
    }

    private HistoryDateRange? SelectedRange() => DeleteFromPicker.Date is { } from && DeleteToPicker.Date is { } to
        ? new HistoryDateRange(DateOnly.FromDateTime(from.DateTime), DateOnly.FromDateTime(to.DateTime)) : null;

    private void DeleteRange_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        _retentionPreview = null;
        RenderRetentionPreviewState();
        RenderRetentionStatus();
    }

    private void StorageWarningBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_renderingStorageSettings || !double.IsFinite(args.NewValue)) return;
        var megabytes = Math.Clamp((int)Math.Round(args.NewValue * 1024d), 103, 1048576);
        QueueStorageSave("retention.warning_mb", megabytes.ToString(CultureInfo.InvariantCulture));
    }

    private async void RetentionMonthsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingStorageSettings || _loadingPolicy || _confirmationOpen
            || RetentionMonthsBox.SelectedItem is not ComboBoxItem { Tag: int months }) return;
        await _storageSaveQueue;
        if (_retentionStatus is not { } status) return;
        if (months > status.MaximumMonths)
        {
            RenderRetentionStatus();
            await Context.Dialogs.ShowPremiumUpgradeAsync(Context.Application, Context.OwnerWindow, _strings.Translate);
            return;
        }
        if (status.DataRetentionDays == months * 30 && status.ScreenshotRetentionDays == months * 30) return;
        var days = (months * 30).ToString(CultureInfo.InvariantCulture);
        var result = await Context.ExecuteAsync((application, token) => application.PatchSettingsAsync(
            new SettingsPatch(new Dictionary<string, string?> { ["retention.data_days"] = days, ["retention.screenshots_days"] = days }), token));
        if (result is { Succeeded: true }) await LoadAsync();
        else RenderRetentionStatus();
    }

    // sender is the screenshot-folder action button.
    // e contains the routed click notification.
    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await _storageSaveQueue;
        await Context.ExecuteAsync((application, token) => application.OpenScreenshotFolderAsync(ScreenshotFolderBox.Text, token));
    }

    // sender is the editable screenshot-folder field.
    // e contains the focus notification that commits the typed path.
    private void ScreenshotFolderBox_LostFocus(object sender, RoutedEventArgs e) =>
        QueueStorageSave("screenshots.directory", ScreenshotFolderBox.Text);

    // sender is the local screenshot-retention toggle.
    // e contains the toggle notification.
    private void KeepScreenshotsSwitch_Toggled(object sender, RoutedEventArgs e) =>
        QueueStorageSave("screenshots.keep", KeepScreenshotsSwitch.IsOn ? "true" : "false");

    // sender is the capture-allowance toggle.
    // e contains the toggle notification.
    private void ScreenshotsEnabledSwitch_Toggled(object sender, RoutedEventArgs e) =>
        QueueStorageSave("screenshots.enabled", ScreenshotsEnabledSwitch.IsOn ? "true" : "false");

    // sender is the hardware-metadata persistence toggle.
    // e contains the toggle notification.
    private void HardwareSaveSnapshotsSwitch_Toggled(object sender, RoutedEventArgs e) =>
        QueueStorageSave("sensors.save_snapshots", HardwareSaveSnapshotsSwitch.IsOn ? "true" : "false");

    // sender is the local screenshot-text extraction toggle.
    // e contains the toggle notification.
    private void OcrEnabledSwitch_Toggled(object sender, RoutedEventArgs e) =>
        QueueStorageSave("ocr.enabled", OcrEnabledSwitch.IsOn ? "true" : "false");

    // sender is the capture-mode selector.
    // e contains the selection notification.
    private void ScreenshotModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenshotModeBox.SelectedItem is ComboBoxItem { Tag: string mode }) QueueStorageSave("screenshots.mode", mode);
    }

    // sender is the local OCR language selector.
    // e contains the selection notification.
    private void OcrLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OcrLanguageBox.SelectedItem is ComboBoxItem { Tag: string language }) QueueStorageSave("ocr.language", language);
    }

    // key is the shared setting changed by the storage controls.
    // value is the typed path or toggle value to validate and persist.
    private void QueueStorageSave(string key, string value)
    {
        if (_renderingStorageSettings || _loadingPolicy || _retentionStatus is not { } status) return;
        var current = key switch
        {
            "screenshots.directory" => status.ScreenshotDirectory,
            "screenshots.keep" => status.KeepScreenshots ? "true" : "false",
            "screenshots.enabled" => status.ScreenshotsEnabled ? "true" : "false",
            "screenshots.mode" => status.ScreenshotCaptureMode,
            "sensors.save_snapshots" => status.HardwareSaveSnapshots ? "true" : "false",
            "ocr.enabled" => status.OcrEnabled ? "true" : "false",
            "ocr.language" => status.OcrLanguage,
            "retention.warning_mb" => status.HistoryWarningMegabytes.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("The retention page cannot submit an undeclared storage preference.")
        };
        var unchanged = value == current;
        if (unchanged && _pendingStorageSaves == 0) return;
        _pendingStorageSaves++;
        _storageSaveQueue = SaveStorageSettingAsync(_storageSaveQueue, key, value);
    }

    // previous preserves the order of queued folder and toggle edits.
    // key is the shared setting being saved.
    // value is the typed value validated by the application facade.
    private async Task SaveStorageSettingAsync(Task previous, string key, string value)
    {
        // Assign the queued task before changing focus or rendering controls during persistence.
        await Task.Yield();
        await previous;
        try
        {
            var result = await Context.ExecuteAsync((application, token) => application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?> { [key] = value }), token), showSuccess: false);
            if (result is { Succeeded: true, Value: { } updated } && _retentionStatus is { } status)
            {
                _retentionStatus = status with
                {
                    ScreenshotDirectory = updated.ScreenshotDirectory,
                    KeepScreenshots = updated.KeepScreenshots,
                    ScreenshotsEnabled = updated.ScreenshotsEnabled,
                    ScreenshotCaptureMode = updated.ScreenshotCaptureMode,
                    OcrEnabled = updated.OcrEnabled,
                    OcrLanguage = updated.OcrLanguage,
                    HardwareSaveSnapshots = updated.HardwareSaveSnapshots,
                    HardwareSensorsEnabled = updated.HardwareSensorsEnabled,
                    HistoryWarningMegabytes = updated.HistoryWarningMegabytes
                };
                _retentionPreview = null;
                RenderRetentionPreviewState();
                Context.ShowStatus(_strings.Translate("Operations.Status.Completed.Title"), _strings.Translate("OptionsSaved"), InfoBarSeverity.Success);
            }
        }
        finally
        {
            // Preserve pending edits until the last save finishes, then restore authoritative values.
            if (--_pendingStorageSaves == 0) RenderRetentionStatus();
        }
    }

    private async void RunRetentionButton_Click(object sender, RoutedEventArgs e)
    {
        await _storageSaveQueue;
        if (_confirmationOpen)
        {
            Context.ShowStatus(
                _strings.Translate("Operations.Retention.ConfirmationOpen.Title"),
                _strings.Translate("Operations.Retention.ConfirmationOpen.Message"),
                InfoBarSeverity.Warning);
            return;
        }

        var range = SelectedRange();
        if (range is not { IsValid: true }) return;
        _confirmationOpen = true;
        RenderRetentionStatus();
        try
        {
            var previewResult = await Context.ExecuteWithProgressAsync(
                (application, token) => application.PreviewRetentionAsync(token, range),
                _strings.Translate("History.ReviewDelete"),
                _strings.Translate("History.DeleteDescription"));
            if (previewResult is not { Succeeded: true, Value: { } preview })
            {
                return;
            }

            RenderRetentionPreview(preview, executed: false);
            if (preview.FileCount == 0) return;
            var confirmed = await Context.Dialogs.ConfirmAsync(
                Context.OwnerWindow,
                DialogRequest.Confirmation(
                    _strings.Translate("Operations.Retention.Confirm.Title"),
                    _strings.Format("History.Confirm", range.From.ToString("d", _strings.Culture), range.To.ToString("d", _strings.Culture),
                        preview.RecordCount, preview.ScreenshotCount, FormatBytes(preview.TotalBytes)),
                    _strings.Translate("History.DeleteAction"),
                    _strings.Translate("Dialog.Cancel")));
            if (!confirmed)
            {
                Context.ShowStatus(
                    _strings.Translate("Operations.Retention.Cancelled.Title"),
                    _strings.Translate("Operations.Retention.Cancelled.Message"),
                    InfoBarSeverity.Informational);
                return;
            }

            var operationId = Guid.NewGuid();
            var runResult = await Context.ExecuteWithProgressAsync(
                (application, token) => application.RunRetentionAsync(new RetentionRequest(Execute: true, Confirmed: true, OperationId: operationId, Range: range), token),
                _strings.Translate("Operations.Retention.Progress.Cleanup.Title"),
                _strings.Translate("History.DeleteProgress"), retentionOperationId: operationId);
            if (runResult is { Succeeded: true, Value: { } deleted })
            {
                RenderRetentionPreview(deleted, executed: true);
                var statusResult = await Context.ExecuteAsync((application, token) => application.GetRetentionStatusAsync(token), showSuccess: false);
                if (statusResult is { Succeeded: true, Value: { } updatedStatus }) _retentionStatus = updatedStatus;
                RenderRetentionStatus();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation stops remaining work; files deleted before the request are not restored.
            _retentionPreview = null;
            RenderRetentionPreviewState();
            Context.ShowStatus(
                _strings.Translate("Operations.Retention.Cancelled.Title"),
                _strings.Translate("Export.Cancelled"),
                InfoBarSeverity.Informational);
        }
        catch (Exception)
        {
            // A dialog-host failure leaves retention untouched and the subsection available.
            Context.ShowStatus(
                _strings.Translate("Operations.Retention.ConfirmationUnavailable.Title"),
                _strings.Translate("Operations.Retention.ConfirmationUnavailable.Message"),
                InfoBarSeverity.Error);
        }
        finally
        {
            _confirmationOpen = false;
            RenderRetentionStatus();
        }
    }

    private void RenderRetentionPreview(RetentionPreview preview, bool executed)
    {
        _retentionPreview = preview;
        _retentionExecuted = executed;
        RenderRetentionPreviewState();
    }

    private void RenderRetentionPreviewState()
    {
        if (_retentionPreview is not { } preview)
        {
            RetentionPreviewText.Text = SelectedRange() is { IsValid: false }
                ? _strings.Translate("HistoryRangeInvalid") : string.Empty;
            return;
        }
        RetentionPreviewText.Text = preview.FileCount == 0 ? _strings.Translate("History.Empty")
            : _strings.Format(_retentionExecuted ? "History.Deleted" : "History.Preview",
                preview.RecordCount, preview.ScreenshotCount, FormatBytes(preview.TotalBytes));
    }

    private string FormatBytes(long bytes)
    {
        var size = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        var value = (double)size;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value.ToString("0.#", _strings.Culture)} {units[unit]}";
    }
}
