// SPDX-License-Identifier: MIT

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
    public RetentionOperationsControl() => InitializeComponent();

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        PreviewPathsExpander.Header = _strings.Translate("Operations.Retention.Preview.Paths");
        CaptureSettingsExpander.Header = _strings.Translate("Options.Section.Snapshots");
        OcrSettingsExpander.Header = _strings.Translate("Options.Ocr.Section");
        TierBadge.Text = TwoMonthsBadge.Text = ThreeMonthsBadge.Text = _strings.Translate("Premium.Badge");
        UiLocalization.SetAccessibleLabel(OpenFolderButton, _strings.Translate("Options.OpenFolderAction"));
        AutomationProperties.SetName(KeepScreenshotsSwitch, _strings.Translate("Options.KeepSnapshots.Header"));
        AutomationProperties.SetName(ScreenshotsEnabledSwitch, _strings.Translate("Options.SnapshotsEnabled.Header"));
        AutomationProperties.SetName(OcrEnabledSwitch, _strings.Translate("Options.Ocr.Enabled"));
        _renderingStorageSettings = true;
        try
        {
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
        AutomationProperties.SetName(RetentionPathsList, _strings.Translate("Operations.Retention.Preview.Paths"));
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
        RetentionPreviewButton.IsEnabled = RetentionCleanupButton.IsEnabled = !_loadingPolicy && _retentionStatus is not null;
        OpenFolderButton.IsEnabled = OneMonthButton.IsEnabled = RetentionPreviewButton.IsEnabled;
        ScreenshotFolderBox.IsEnabled = KeepScreenshotsSwitch.IsEnabled = RetentionPreviewButton.IsEnabled;
        ScreenshotsEnabledSwitch.IsEnabled = ScreenshotModeBox.IsEnabled = OcrEnabledSwitch.IsEnabled = RetentionPreviewButton.IsEnabled;
        OcrLanguageBox.IsEnabled = RetentionPreviewButton.IsEnabled && _retentionStatus?.OcrEnabled == true;
        HardwareSaveSnapshotsSwitch.IsEnabled = RetentionPreviewButton.IsEnabled && _retentionStatus?.HardwareSensorsEnabled == true;
        TwoMonthsButton.IsEnabled = ThreeMonthsButton.IsEnabled = RetentionPreviewButton.IsEnabled && _retentionStatus?.MaximumMonths == 3;
        TierBadge.Visibility = _retentionStatus?.MaximumMonths == 3 ? Visibility.Visible : Visibility.Collapsed;
        OneMonthButton.IsChecked = _retentionStatus?.DataRetentionDays == 30;
        TwoMonthsButton.IsChecked = _retentionStatus?.DataRetentionDays == 60;
        ThreeMonthsButton.IsChecked = _retentionStatus?.DataRetentionDays == 90;
        AutomationProperties.SetName(OneMonthButton, _strings.Translate("Operations.Retention.OneMonth"));
        AutomationProperties.SetName(TwoMonthsButton, _strings.Translate("Operations.Retention.TwoMonths") + " · " + _strings.Translate("Premium.Badge"));
        AutomationProperties.SetName(ThreeMonthsButton, _strings.Translate("Operations.Retention.ThreeMonths") + " · " + _strings.Translate("Premium.Badge"));
        FirstActivationText.Text = FormatDate(_retentionStatus?.FirstActivationDate);
        LastCleanupText.Text = _retentionStatus?.LastCleanupDate is { } last ? FormatDate(last) : _strings.Translate("Operations.Retention.Never");
        NextCleanupText.Text = FormatDate(_retentionStatus?.NextCleanupDate);
        MonthlyScheduleText.Text = _retentionStatus?.FirstActivationDate is { } first
            ? _strings.Format("Operations.Retention.MonthlyDay", first.Day) : string.Empty;
        if (_retentionStatus is { } status)
        {
            RetentionStatusText.Text = _strings.Format(
                "Operations.Retention.StatusMonths",
                status.DataRetentionDays / 30,
                status.ScreenshotRetentionDays / 30);
            _renderingStorageSettings = true;
            try
            {
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

    // date is the local schedule date, or an unavailable value rendered as an em dash.
    private string FormatDate(DateOnly? date) => date?.ToString("D", _strings.Culture) ?? "—";

    // sender identifies the selected one-, two-, or three-month choice.
    // e contains the routed click notification.
    private async void RetentionMonth_Click(object sender, RoutedEventArgs e)
    {
        await _storageSaveQueue;
        var months = ReferenceEquals(sender, OneMonthButton) ? 1 : ReferenceEquals(sender, TwoMonthsButton) ? 2 : 3;
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
                    HardwareSensorsEnabled = updated.HardwareSensorsEnabled
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

    private async void RetentionPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await _storageSaveQueue;
        var result = await Context.ExecuteWithProgressAsync(
            (application, token) => application.PreviewRetentionAsync(token),
            _strings.Translate("Operations.Retention.PreviewAction"),
            _strings.Translate("Operations.Retention.Preview.Description"));
        if (result is { Succeeded: true, Value: { } preview })
        {
            RenderRetentionPreview(preview, executed: false);
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

        _confirmationOpen = true;
        try
        {
            var previewResult = await Context.ExecuteWithProgressAsync(
                (application, token) => application.PreviewRetentionAsync(token),
                _strings.Translate("Operations.Retention.PreviewAction"),
                _strings.Translate("Operations.Retention.Preview.Description"));
            if (previewResult is not { Succeeded: true, Value: { } preview })
            {
                return;
            }

            RenderRetentionPreview(preview, executed: false);
            var confirmed = await Context.Dialogs.ConfirmAsync(
                Context.OwnerWindow,
                DialogRequest.Confirmation(
                    _strings.Translate("Operations.Retention.Confirm.Title"),
                    _strings.Format("Operations.Retention.Confirm.Message", preview.FileCount, FormatBytes(preview.TotalBytes)),
                    _strings.Translate("Dialog.Ok"),
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
                (application, token) => application.RunRetentionAsync(new RetentionRequest(Execute: true, Confirmed: true, OperationId: operationId), token),
                _strings.Translate("Operations.Retention.Progress.Cleanup.Title"),
                _strings.Translate("Operations.Retention.ProgressDescription"), retentionOperationId: operationId);
            if (runResult is { Succeeded: true, Value: { } deleted })
            {
                RenderRetentionPreview(deleted, executed: true);
                var statusResult = await Context.Application.GetRetentionStatusAsync(System.Threading.CancellationToken.None);
                if (statusResult is { Succeeded: true, Value: { } updatedStatus }) _retentionStatus = updatedStatus;
                RenderRetentionStatus();
            }
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
            RetentionPreviewText.Text = _strings.Translate("Operations.Initial.RetentionPreview");
            RetentionPathsList.ItemsSource = null;
            RecordsPreviewText.Text = ScreenshotsPreviewText.Text = TotalPreviewText.Text = "—";
            return;
        }

        RetentionPreviewText.Text = _retentionExecuted
            ? _strings.Format("Operations.Retention.Deleted", preview.FileCount, FormatBytes(preview.TotalBytes))
            : _strings.Format("Operations.Retention.Eligible", preview.FileCount, FormatBytes(preview.TotalBytes));
        RetentionPathsList.ItemsSource = preview.Paths.ToArray();
        RecordsPreviewText.Text = _strings.Format("Operations.Retention.RecordSummary", preview.RecordCount, FormatBytes(preview.ActivityBytes));
        ScreenshotsPreviewText.Text = _strings.Format("Operations.Retention.ScreenshotSummary", preview.ScreenshotCount, FormatBytes(preview.ScreenshotBytes));
        TotalPreviewText.Text = FormatBytes(preview.TotalBytes);
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
