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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects operational input and renders DTOs returned by the shared application facade.</summary>
public sealed partial class OperationsControl : UserControl
{
    private IWorkTrailApplication? _application;
    private MicaDialogService? _dialogs;
    private Window? _ownerWindow;
    private TimedInfoBar? _notificationHost;
    private LocalizationService _strings = new("system");
    private bool _operationInProgress;
    private bool _returnToOverviewOnBack;
    private Control? _lastLandingLink;
    private SnapshotAiOperationsControl? _snapshotAiSection;
    private PrivacyOperationsControl? _privacySection;
    private RetentionOperationsControl? _retentionSection;
    private PluginOperationsControl? _pluginsSection;
    private InstallationTransferOperationsControl? _installationTransferSection;

    /// <summary>Creates the passive operational surface.</summary>
    public OperationsControl() => InitializeComponent();

    /// <summary>Occurs when back navigation is requested from the tools landing page.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Occurs after the visible operational page changes and the host may need to re-measure.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Indicates whether the archive page is visible so the host can mark its title bar.</summary>
    internal bool IsArchivePageVisible => DetailScroll.Visibility == Visibility.Visible
        && _installationTransferSection?.Visibility == Visibility.Visible;

    /// <summary>Occurs after the runtime has accepted both confirmations and prepared the reset plan.</summary>
    internal event EventHandler<AtomicResetPreparedEventArgs>? AtomicResetPrepared;

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        _snapshotAiSection?.ApplyLanguage(language);
        _privacySection?.ApplyLanguage(language);
        _retentionSection?.ApplyLanguage(language);
        _pluginsSection?.ApplyLanguage(language);
        _installationTransferSection?.ApplyLanguage(language);
        ApplyNavigationAccessibility(OpenSnapshotAiLink, "Options.Navigation.SnapshotAi.Action", "Options.Navigation.SnapshotAi.Description");
        ApplyNavigationAccessibility(OpenPrivacyLink, "Options.Navigation.Privacy.Action", "Options.Navigation.Privacy.Description");
        ApplyNavigationAccessibility(OpenRetentionLink, "Options.Navigation.Retention.Action", "Options.Navigation.Retention.Description");
        ApplyNavigationAccessibility(OpenPluginsLink, "Options.Navigation.Plugins.Action", "Options.Navigation.Plugins.Description");
        ApplyOptionalNavigationAccessibility(
            OpenInstallationTransferLink,
            "Operations.InstallationTransfer.Navigation.Action",
            "Manage installations and archives",
            "Operations.InstallationTransfer.Navigation.Description",
            "Name installations and transfer local data and screenshots between systems.");
        AutomationProperties.SetName(OperationProgress, _strings.Translate("Operations.Status.InProgress.Title"));
        AutomationProperties.SetName(AtomicNukeButton, _strings.Translate("Operations.AtomicNuke.Action"));
        AutomationProperties.SetHelpText(AtomicNukeButton, _strings.Translate("Operations.AtomicNuke.Description"));
    }

    /// <summary>Connects the surface to the facade owned by the composition root.</summary>
    internal void Initialize(IWorkTrailApplication application, MicaDialogService dialogs, Window ownerWindow, TimedInfoBar notificationHost)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _notificationHost = notificationHost ?? throw new ArgumentNullException(nameof(notificationHost));
    }

    /// <summary>Returns to the landing page for local tool navigation, or to the surface that opened a direct settings link.</summary>
    public void NavigateBack()
    {
        if (DetailScroll.Visibility == Visibility.Visible && _returnToOverviewOnBack)
        {
            ShowOverview(restoreFocus: true);
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a focused operational page and loads its current settings without mutating application state.</summary>
    internal void NavigateTo(OperationsSection section, bool returnToOverview = true)
    {
        _returnToOverviewOnBack = returnToOverview;
        var selectedSection = EnsureSection(section);
        HideDetailSections();
        OperationsScroll.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
        selectedSection.Visibility = Visibility.Visible;
        DetailScroll.ChangeView(null, 0, null, disableAnimation: true);
        NotifyLayoutChanged();
        if (section == OperationsSection.Plugins)
        {
            _ = _pluginsSection!.LoadAsync();
        }
        else if (section == OperationsSection.Retention)
        {
            _ = _retentionSection!.LoadAsync();
        }
    }

    /// <summary>Shows the tools landing page without changing application state.</summary>
    internal void ShowOverview() => ShowOverview(restoreFocus: false);

    private IWorkTrailApplication Application => _application ?? throw new InvalidOperationException("OperationsControl must be initialized before use.");

    private MicaDialogService Dialogs => _dialogs ?? throw new InvalidOperationException("OperationsControl must be initialized before use.");

    private Window OwnerWindow => _ownerWindow ?? throw new InvalidOperationException("OperationsControl must be initialized before use.");

    private TimedInfoBar OperationBanner => _notificationHost ?? throw new InvalidOperationException("OperationsControl must be initialized before use.");

    private void OpenSnapshotAiLink_Click(object sender, RoutedEventArgs e) => OpenSection(OperationsSection.SnapshotAi, sender);


    private void OpenPrivacyLink_Click(object sender, RoutedEventArgs e) => OpenSection(OperationsSection.Privacy, sender);

    private void OpenRetentionLink_Click(object sender, RoutedEventArgs e) => OpenSection(OperationsSection.Retention, sender);

    private void OpenPluginsLink_Click(object sender, RoutedEventArgs e) => OpenSection(OperationsSection.Plugins, sender);

    private void OpenInstallationTransferLink_Click(object sender, RoutedEventArgs e)
    {
        _lastLandingLink = sender as Control;
        NavigateToInstallationTransfer(returnToOverview: true);
    }

    /// <summary>Shows installation identity and archive transfer tools without changing local data.</summary>
    internal void NavigateToInstallationTransfer(bool returnToOverview)
    {
        _returnToOverviewOnBack = returnToOverview;
        OperationsScroll.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
        HideDetailSections();
        var section = EnsureInstallationTransferSection();
        section.ShowArchiveActions();
        section.Visibility = Visibility.Visible;
        DetailScroll.ChangeView(null, 0, null, disableAnimation: true);
        NotifyLayoutChanged();
    }

    /// <summary>Starts the export picker on the visible installation-transfer surface.</summary>
    internal Task StartInstallationArchiveExportAsync() =>
        EnsureInstallationTransferSection().StartExportAsync();

    /// <summary>Starts the import picker and safe archive preview on the visible installation-transfer surface.</summary>
    internal Task StartInstallationArchiveImportAsync() =>
        EnsureInstallationTransferSection().StartImportPreviewAsync();

    private async void AtomicNukeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_operationInProgress)
            {
                ShowStatus(
                    _strings.Translate("Operations.Status.InProgress.Title"),
                    _strings.Translate("Operations.Status.InProgress.Message"),
                    InfoBarSeverity.Warning);
                return;
            }

            var firstConfirmation = await Dialogs.ConfirmAtomicResetAsync(
                OwnerWindow,
                DialogRequest.Confirmation(
                    _strings.Translate("Operations.AtomicNuke.First.Title"),
                    _strings.Translate("Operations.AtomicNuke.First.Message"),
                    _strings.Translate("Dialog.Ok"),
                    _strings.Translate("Dialog.Cancel")));
            if (!firstConfirmation)
            {
                return;
            }

            var finalConfirmation = await Dialogs.ConfirmAtomicResetAsync(
                OwnerWindow,
                DialogRequest.Confirmation(
                    _strings.Translate("Operations.AtomicNuke.Second.Title"),
                    _strings.Translate("Operations.AtomicNuke.Second.Message"),
                    _strings.Translate("Dialog.Ok"),
                    _strings.Translate("Dialog.Cancel")));
            if (!finalConfirmation)
            {
                return;
            }

            var result = await ExecuteAsync((application, token) => application.PrepareAtomicResetAsync(
                new AtomicResetRequest(firstConfirmation, finalConfirmation),
                token));
            if (result is { Succeeded: true, Value: { } plan })
            {
                AtomicResetPrepared?.Invoke(this, new AtomicResetPreparedEventArgs(plan));
            }
        }
        catch (Exception)
        {
            // A confirmation-host failure must leave the app running and never prepare a reset.
            ShowStatus(
                _strings.Translate("Operations.Status.RuntimeUnavailable.Title"),
                _strings.Translate("Operations.Status.RuntimeUnavailable.Message"),
                InfoBarSeverity.Error);
        }
    }

    private void OpenSection(OperationsSection section, object sender)
    {
        _lastLandingLink = sender as Control;
        NavigateTo(section);
    }

    private void ShowOverview(bool restoreFocus)
    {
        _returnToOverviewOnBack = false;
        HideDetailSections();
        DetailScroll.Visibility = Visibility.Collapsed;
        OperationsScroll.Visibility = Visibility.Visible;
        OperationsScroll.ChangeView(null, 0, null, disableAnimation: true);
        NotifyLayoutChanged();
        if (restoreFocus)
        {
            _lastLandingLink?.Focus(FocusState.Programmatic);
        }
    }

    private FrameworkElement EnsureSection(OperationsSection section) => section switch
    {
        OperationsSection.SnapshotAi => EnsureSnapshotAiSection(),
        OperationsSection.Privacy => EnsurePrivacySection(),
        OperationsSection.Retention => EnsureRetentionSection(),
        OperationsSection.Plugins => EnsurePluginsSection(),
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unsupported operations section.")
    };

    private SnapshotAiOperationsControl EnsureSnapshotAiSection()
    {
        if (_snapshotAiSection is not null)
        {
            return _snapshotAiSection;
        }

        _snapshotAiSection = new SnapshotAiOperationsControl();
        SnapshotAiHost.Content = _snapshotAiSection;
        _snapshotAiSection.Initialize(Application, Dialogs, OwnerWindow, OperationBanner);
        _snapshotAiSection.ApplyLanguage(_strings.Language);
        return _snapshotAiSection;
    }

    private PrivacyOperationsControl EnsurePrivacySection()
    {
        if (_privacySection is not null)
        {
            return _privacySection;
        }

        _privacySection = new PrivacyOperationsControl();
        PrivacyHost.Content = _privacySection;
        _privacySection.Initialize(Application, Dialogs, OwnerWindow, OperationBanner);
        _privacySection.ApplyLanguage(_strings.Language);
        return _privacySection;
    }

    private RetentionOperationsControl EnsureRetentionSection()
    {
        if (_retentionSection is not null)
        {
            return _retentionSection;
        }

        _retentionSection = new RetentionOperationsControl();
        RetentionHost.Content = _retentionSection;
        _retentionSection.Initialize(Application, Dialogs, OwnerWindow, OperationBanner);
        _retentionSection.ApplyLanguage(_strings.Language);
        return _retentionSection;
    }

    private PluginOperationsControl EnsurePluginsSection()
    {
        if (_pluginsSection is not null)
        {
            return _pluginsSection;
        }

        _pluginsSection = new PluginOperationsControl();
        PluginsHost.Content = _pluginsSection;
        _pluginsSection.Initialize(Application, Dialogs, OwnerWindow, OperationBanner);
        _pluginsSection.ApplyLanguage(_strings.Language);
        return _pluginsSection;
    }

    private InstallationTransferOperationsControl EnsureInstallationTransferSection()
    {
        if (_installationTransferSection is not null)
        {
            return _installationTransferSection;
        }

        _installationTransferSection = new InstallationTransferOperationsControl();
        InstallationTransferHost.Content = _installationTransferSection;
        _installationTransferSection.Initialize(Application, Dialogs, OwnerWindow, OperationBanner);
        _installationTransferSection.ApplyLanguage(_strings.Language);
        return _installationTransferSection;
    }

    private void HideDetailSections()
    {
        _snapshotAiSection?.Visibility = Visibility.Collapsed;
        _privacySection?.Visibility = Visibility.Collapsed;
        _retentionSection?.Visibility = Visibility.Collapsed;
        _pluginsSection?.Visibility = Visibility.Collapsed;
        _installationTransferSection?.Visibility = Visibility.Collapsed;
    }

    private void ApplyNavigationAccessibility(Control link, string actionKey, string descriptionKey)
    {
        AutomationProperties.SetName(link, _strings.Translate(actionKey));
        AutomationProperties.SetHelpText(link, _strings.Translate(descriptionKey));
    }

    private void ApplyOptionalNavigationAccessibility(
        Control link,
        string actionKey,
        string actionFallback,
        string descriptionKey,
        string descriptionFallback)
    {
        AutomationProperties.SetName(link, _strings.TryTranslate(actionKey, out var action) ? action : actionFallback);
        AutomationProperties.SetHelpText(link, _strings.TryTranslate(descriptionKey, out var description) ? description : descriptionFallback);
    }

    private void NotifyLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    private async Task<OperationResult<T>?> ExecuteAsync<T>(Func<IWorkTrailApplication, CancellationToken, Task<OperationResult<T>>> operation)
    {
        if (_operationInProgress)
        {
            ShowStatus(
                _strings.Translate("Operations.Status.InProgress.Title"),
                _strings.Translate("Operations.Status.InProgress.Message"),
                InfoBarSeverity.Warning);
            return null;
        }

        _operationInProgress = true;
        OperationsScroll.IsEnabled = false;
        OperationProgress.IsActive = true;
        OperationProgress.Visibility = Visibility.Visible;
        try
        {
            var result = await operation(Application, CancellationToken.None);
            if (result.Succeeded)
            {
                ShowStatus(
                    _strings.Translate("Operations.Status.Completed.Title"),
                    ResultMessage(result.MessageKey, succeeded: true),
                    InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus(
                    _strings.Translate("Operations.Status.Failed.Title"),
                    ResultMessage(result.MessageKey, succeeded: false),
                    InfoBarSeverity.Error);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // Presentation remains usable if the shared runtime cancels an operation.
            ShowStatus(
                _strings.Translate("Operations.Status.Cancelled.Title"),
                _strings.Translate("Operations.Status.Cancelled.Message"),
                InfoBarSeverity.Warning);
            return null;
        }
        catch (Exception)
        {
            // Runtime failures are rendered without leaking implementation or host details into the UI.
            ShowStatus(
                _strings.Translate("Operations.Status.RuntimeUnavailable.Title"),
                _strings.Translate("Operations.Status.RuntimeUnavailable.Message"),
                InfoBarSeverity.Error);
            return null;
        }
        finally
        {
            OperationProgress.IsActive = false;
            OperationProgress.Visibility = Visibility.Collapsed;
            OperationsScroll.IsEnabled = true;
            _operationInProgress = false;
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        switch (severity)
        {
            case InfoBarSeverity.Success:
                Dialogs.Notifications.ShowSuccess(OperationBanner, title, message);
                break;
            case InfoBarSeverity.Error:
                Dialogs.Notifications.ShowError(OperationBanner, title, message);
                break;
            case InfoBarSeverity.Warning:
                Dialogs.Notifications.ShowWarning(OperationBanner, title, message);
                break;
            default:
                Dialogs.Notifications.ShowInfo(OperationBanner, title, message);
                break;
        }
    }

    private string ResultMessage(string messageKey, bool succeeded)
    {
        return _strings.TryTranslate(messageKey, out var localized)
            ? localized
            : _strings.Translate(succeeded ? "Operations.Result.Success" : "Operations.Result.Failure");
    }

}

/// <summary>Contains the validated reset plan returned by the runtime owner.</summary>
internal sealed class AtomicResetPreparedEventArgs(AtomicResetPlan plan) : EventArgs
{
    internal AtomicResetPlan Plan { get; } = plan;
}
