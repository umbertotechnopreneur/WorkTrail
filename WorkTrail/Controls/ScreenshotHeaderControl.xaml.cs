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
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Displays the screenshot date, metadata, command bar, privacy status, and date selector.</summary>
public sealed partial class ScreenshotHeaderControl : UserControl
{
    private LocalizationService _strings = new("system");
    private bool _hasImage;
    private bool _analysisDeletionAvailable;
    private bool _deletionActionsEnabled = true;

    /// <summary>Creates the header control.</summary>
    public ScreenshotHeaderControl() => InitializeComponent();

    /// <summary>Raised when the user requests a smaller screenshot zoom.</summary>
    public event EventHandler? ZoomOutRequested;

    /// <summary>Raised when the user requests the base screenshot zoom.</summary>
    public event EventHandler? ZoomResetRequested;

    /// <summary>Raised when the user requests a larger screenshot zoom.</summary>
    public event EventHandler? ZoomInRequested;

    /// <summary>Raised when the user changes the requested snapshot-details visibility.</summary>
    public event Action<bool>? DetailsVisibilityRequested;

    /// <summary>Raised when the user asks the host window to export the displayed screenshot.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised when the user asks the host window to share the displayed screenshot.</summary>
    public event EventHandler? ShareRequested;

    /// <summary>Raised when the user asks the host window to open the screenshot folder.</summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>Raised when the user asks the host window to delete the displayed screenshot file.</summary>
    public event EventHandler? DeleteScreenshotRequested;

    /// <summary>Raised when the user asks the host window to delete the displayed screenshot metadata.</summary>
    public event EventHandler? DeleteAnalysisRequested;

    /// <summary>Gets the date picker used by the host window.</summary>
    public CalendarDatePicker DatePicker => SelectedDatePicker;

    /// <summary>Gets the text element that displays the screenshot count.</summary>
    public TextBlock CountText => GalleryCountText;

    /// <summary>Gets the large text element that displays the selected date.</summary>
    public TextBlock DisplayDateText => ExtendedDateText;

    /// <summary>Applies localized labels to every icon-only command in the native toolbar.</summary>
    public void ApplyToolbarLocalization(LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        _strings = strings;
        SetCommandLabel(ZoomOutButton, "Screenshots.Toolbar.ZoomOut");
        SetCommandLabel(ZoomResetButton, "Screenshots.Toolbar.ZoomReset");
        SetCommandLabel(ZoomInButton, "Screenshots.Toolbar.ZoomIn");
        SetCommandLabel(SaveButton, "Screenshots.Toolbar.Save");
        SetCommandLabel(ShareButton, "Screenshots.Toolbar.Share");
        SetCommandLabel(OpenFolderButton, "Screenshots.Toolbar.OpenFolder");
        SetCommandLabel(DeleteScreenshotButton, "Screenshots.Toolbar.DeleteScreenshot");
        SetCommandLabel(DeleteAnalysisButton, "Screenshots.Toolbar.DeleteAnalysis");
        AutomationProperties.SetName(ScreenshotToolbar, _strings.Translate("Screenshots.Metadata"));
        UpdateMetadataAccessibility();
        UpdateInstallationAccessibility();
    }

    /// <summary>Renders the selected capture metadata as plain toolbar content.</summary>
    public void SetMetadata(
        string dateText,
        string timeText,
        string applicationText,
        InstallationProfile installation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dateText);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeText);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationText);
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.FriendlyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installation.MachineName);
        MetadataDateValueText.Text = dateText;
        MetadataTimeValueText.Text = timeText;
        MetadataAppValueText.Text = applicationText;
        MetadataPanel.Visibility = Visibility.Visible;
        InstallationFriendlyNameText.Text = installation.FriendlyName;
        InstallationMachineNameText.Text = installation.MachineName;
        var accentBrush = InstallationAppearance.CreateAccentBrush(installation.Color);
        InstallationIconBadge.BorderBrush = accentBrush;
        InstallationIcon.Foreground = accentBrush;
        InstallationIcon.Glyph = InstallationAppearance.GetIconGlyph(installation.Icon);
        InstallationProvenanceBadge.Visibility = Visibility.Visible;
        UpdateMetadataAccessibility();
        UpdateInstallationAccessibility();
    }

    /// <summary>Clears toolbar metadata when no screenshot is selected.</summary>
    public void ClearMetadata()
    {
        MetadataDateValueText.Text = "--";
        MetadataTimeValueText.Text = "--";
        MetadataAppValueText.Text = "--";
        MetadataPanel.Visibility = Visibility.Collapsed;
        InstallationFriendlyNameText.Text = string.Empty;
        InstallationMachineNameText.Text = string.Empty;
        InstallationProvenanceBadge.Visibility = Visibility.Collapsed;
        UpdateMetadataAccessibility();
        UpdateInstallationAccessibility();
    }

    /// <summary>Synchronizes zoom and selected-image command state with the passive viewer.</summary>
    public void SetViewerState(string zoomText, bool hasImage, bool canZoomOut, bool canZoomIn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoomText);
        ZoomPercentIcon.Glyph = zoomText;
        ZoomOutButton.IsEnabled = canZoomOut;
        ZoomResetButton.IsEnabled = hasImage;
        ZoomInButton.IsEnabled = canZoomIn;
        SaveButton.IsEnabled = hasImage;
        ShareButton.IsEnabled = hasImage;
        _hasImage = hasImage;
        UpdateDeletionActionState();
    }

    /// <summary>Prevents overlapping destructive commands while the host confirms and completes one deletion.</summary>
    public void SetDeletionActionsEnabled(bool isEnabled)
    {
        _deletionActionsEnabled = isEnabled;
        UpdateDeletionActionState();
    }

    /// <summary>Shows whether the selected image still owns removable OCR, AI, or interval telemetry.</summary>
    public void SetAnalysisDeletionAvailable(bool isAvailable)
    {
        _analysisDeletionAvailable = isAvailable;
        UpdateDeletionActionState();
    }

    /// <summary>Synchronizes the snapshot-details command without owning the preference.</summary>
    public void SetDetailsState(bool isEnabled, bool isVisible, string localizedLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedLabel);
        DetailsToggleButton.IsEnabled = isEnabled;
        DetailsToggleButton.IsChecked = isVisible;
        DetailsToggleButton.Label = localizedLabel;
        DetailsToggleButton.Tag = isVisible ? "Screenshots.Details.Hide" : "Screenshots.Details.Show";
        AutomationProperties.SetName(DetailsToggleButton, localizedLabel);
        ToolTipService.SetToolTip(DetailsToggleButton, localizedLabel);
    }

    /// <summary>Renders the localized application-wide privacy-filter status.</summary>
    public void SetPrivacyStatus(string statusText, bool hasActiveRules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusText);
        PrivacyStatusText.Text = statusText;
        PrivacyStatusBadge.Opacity = hasActiveRules ? 1d : 0.72d;
        AutomationProperties.SetName(PrivacyStatusBadge, statusText);
        ToolTipService.SetToolTip(PrivacyStatusBadge, statusText);
    }

    private void SetCommandLabel(DependencyObject command, string key)
    {
        var label = _strings.Translate(key);
        switch (command)
        {
            case AppBarButton button:
                button.Label = label;
                break;
            case AppBarToggleButton toggleButton:
                toggleButton.Label = label;
                break;
        }

        UiLocalization.SetAccessibleLabel(command, label);
    }

    private void UpdateMetadataAccessibility()
    {
        AutomationProperties.SetName(MetadataDateItem, $"{_strings.Translate("Screenshots.DateLabel")}: {MetadataDateValueText.Text}");
        AutomationProperties.SetName(MetadataTimeItem, $"{_strings.Translate("Screenshots.TimeLabel")}: {MetadataTimeValueText.Text}");
        AutomationProperties.SetName(MetadataApplicationItem, $"{_strings.Translate("Screenshots.ApplicationLabel")}: {MetadataAppValueText.Text}");
    }

    private void UpdateInstallationAccessibility()
    {
        var accessibleName = InstallationProvenanceBadge.Visibility == Visibility.Visible
            ? $"{_strings.Translate("Screenshots.Installation")}: {InstallationFriendlyNameText.Text} · {InstallationMachineNameText.Text}"
            : _strings.Translate("Screenshots.Installation");
        AutomationProperties.SetName(InstallationProvenanceBadge, accessibleName);
        AutomationProperties.SetHelpText(InstallationProvenanceBadge, accessibleName);
        ToolTipService.SetToolTip(InstallationProvenanceBadge, accessibleName);
    }

    private void UpdateDeletionActionState()
    {
        var enabled = _hasImage && _deletionActionsEnabled;
        DeleteScreenshotButton.IsEnabled = enabled;
        DeleteAnalysisButton.IsEnabled = enabled && _analysisDeletionAvailable;
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    private void ZoomResetButton_Click(object sender, RoutedEventArgs e) => ZoomResetRequested?.Invoke(this, EventArgs.Empty);

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    private void DetailsToggleButton_Click(object sender, RoutedEventArgs e) =>
        DetailsVisibilityRequested?.Invoke(DetailsToggleButton.IsChecked == true);

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void ShareButton_Click(object sender, RoutedEventArgs e) => ShareRequested?.Invoke(this, EventArgs.Empty);

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private void DeleteScreenshotButton_Click(object sender, RoutedEventArgs e) => DeleteScreenshotRequested?.Invoke(this, EventArgs.Empty);

    private void DeleteAnalysisButton_Click(object sender, RoutedEventArgs e) => DeleteAnalysisRequested?.Invoke(this, EventArgs.Empty);
}
