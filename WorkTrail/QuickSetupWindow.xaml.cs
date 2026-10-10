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


using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WorkTrail.Application;
using WorkTrail.Controls;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Collects one Quick Setup profile choice and delegates its atomic application to the shared facade.</summary>
internal sealed partial class QuickSetupWindow : Window
{
    private const int LogicalWindowWidth = 1000;
    private const int LogicalWindowHeight = 760;
    private const int LogicalScreenMargin = 24;
    private readonly IWorkTrailApplication _application;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly LocalizationService _strings;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly bool _firstRun;
    private readonly string _providerName;
    private string _selectedProfileId;
    private bool _applying;
    private bool _aiVerified;
    private bool _applyAfterVerification;

    /// <summary>Occurs after the application layer persists a complete Quick Setup profile.</summary>
    internal event Action<AppSettings>? ProfileApplied;

    /// <summary>Creates the owned acrylic Quick Setup window from the current validated settings snapshot.</summary>
    internal QuickSetupWindow(
        IWorkTrailApplication application,
        AppSettings settings,
        bool firstRun,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        _firstRun = firstRun;
        _providerName = settings.AiProvider.ToLowerInvariant() switch
        {
            "openai" => "OpenAI",
            "openrouter" => "OpenRouter",
            "anthropic" => "Anthropic",
            _ => throw new InvalidOperationException("The configured AI provider is unsupported.")
        };
        _strings = new LocalizationService(settings.UiLanguage);
        _selectedProfileId = firstRun ? QuickSetupProfileIds.Complete : InferProfile(settings);

        InitializeComponent();
        RootGrid.RequestedTheme = settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            () => []);
        _placement = new WindowPlacementService(
            application,
            this,
            _appWindow,
            WindowStateKeys.QuickSetup,
            LogicalWindowWidth,
            LogicalWindowHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        WindowInteropService.SetOwner(windowHandle, ownerHandle);
        ConfigureWindowBehavior();
        _placement.ApplyDefaultBounds(RootGrid);

        StartWithWindowsCheckBox.IsChecked = firstRun || settings.StartWithWindows;
        ApplyLanguage();
        UpdateSelection();
        Closed += QuickSetupWindow_Closed;
    }

    private void ApplyLanguage()
    {
        UiLocalization.Apply(RootGrid, _strings);
        Title = T("QuickSetup.Title");
        PrimaryButton.Content = T(_firstRun ? "ProviderSetup.Continue" : "QuickSetup.Apply");
        ApiSetupButton.Content = _providerName + " API";
        if (_firstRun) WelcomeText.Text = T("ProviderSetup.Welcome");
        AutomationProperties.SetName(PrimaryButton, PrimaryButton.Content?.ToString() ?? T("QuickSetup.Apply"));
        AutomationProperties.SetName(CompleteProfileButton, T("QuickSetup.Profile.Complete.Accessible"));
        AutomationProperties.SetName(AssistedProfileButton, T("QuickSetup.Profile.Assisted.Accessible"));
        AutomationProperties.SetName(LocalRecordProfileButton, T("QuickSetup.Profile.LocalRecord.Accessible"));
        AutomationProperties.SetName(EssentialOfflineProfileButton, T("QuickSetup.Profile.EssentialOffline.Accessible"));
        SelectedProfileLabelText.Text = T("QuickSetup.SelectedProfile");
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        // ToggleButton content enters the visual tree only after its template is realized.
        ApplyLanguage();
        UpdateSelection();
        await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
        SelectedButton().Focus(FocusState.Programmatic);
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string profileId })
        {
            throw new InvalidOperationException("A Quick Setup profile button must declare its profile identifier.");
        }

        _selectedProfileId = profileId;
        ApplyInfoBar.IsOpen = false;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        CompleteProfileButton.IsChecked = _selectedProfileId == QuickSetupProfileIds.Complete;
        AssistedProfileButton.IsChecked = _selectedProfileId == QuickSetupProfileIds.Assisted;
        LocalRecordProfileButton.IsChecked = _selectedProfileId == QuickSetupProfileIds.LocalRecord;
        EssentialOfflineProfileButton.IsChecked = _selectedProfileId == QuickSetupProfileIds.EssentialOffline;
        SelectedProfileNameText.Text = T(ProfileTitleKey(_selectedProfileId));
        SelectedProfileDescriptionText.Text = T(ProfileDescriptionKey(_selectedProfileId));
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_applying)
        {
            return;
        }

        if (_selectedProfileId is QuickSetupProfileIds.Complete or QuickSetupProfileIds.Assisted && !_aiVerified)
        {
            await ShowApiSetupAsync(applyAfterVerification: true);
            return;
        }

        await ApplyProfileAsync();
    }

    private async void ApiSetupButton_Click(object sender, RoutedEventArgs e) =>
        await ShowApiSetupAsync(applyAfterVerification: false);

    private async Task ShowApiSetupAsync(bool applyAfterVerification)
    {
        _applyAfterVerification = applyAfterVerification;
        _aiVerified = false;
        var setup = new ProviderKeySetupControl();
        setup.Dismissed += ApiSetup_Dismissed;
        setup.WithoutAiRequested += ApiSetup_WithoutAiRequested;
        ProfileSurface.Visibility = Visibility.Collapsed;
        KeySetupHost.Content = setup;
        KeySetupHost.Visibility = Visibility.Visible;
        await setup.InitializeAsync(_application, _strings, weather: false, _lifetimeCancellation.Token);
    }

    private void ReturnToProfiles()
    {
        KeySetupHost.Content = null;
        KeySetupHost.Visibility = Visibility.Collapsed;
        ProfileSurface.Visibility = Visibility.Visible;
        SelectedButton().Focus(FocusState.Programmatic);
    }

    private async void ApiSetup_Dismissed(bool verified)
    {
        _aiVerified = verified;
        ReturnToProfiles();
        if (verified && _applyAfterVerification) await ApplyProfileAsync();
    }

    private void ApiSetup_WithoutAiRequested()
    {
        _selectedProfileId = _selectedProfileId == QuickSetupProfileIds.Complete
            ? QuickSetupProfileIds.LocalRecord : QuickSetupProfileIds.EssentialOffline;
        _aiVerified = false;
        UpdateSelection();
        ReturnToProfiles();
    }

    private async Task ApplyProfileAsync()
    {

        _applying = true;
        SetActionsEnabled(false);
        ApplyInfoBar.IsOpen = false;
        try
        {
            var result = await _application.ApplyQuickSetupProfileAsync(
                new QuickSetupProfileRequest(
                    _selectedProfileId,
                    StartWithWindowsCheckBox.IsChecked == true),
                _lifetimeCancellation.Token);
            if (!result.Succeeded || result.Value is null)
            {
                ApplyInfoBar.Title = T("QuickSetup.Error.Title");
                ApplyInfoBar.Message = result.Code == "quick_setup.ai.verification_required"
                    ? T("ProviderSetup.Error.VerificationRequired")
                    : result.Issues.Any(issue =>
                        issue.Field == "ai.enabled" && issue.Code == "api_key_required")
                    ? T("QuickSetup.Error.AiKey")
                    : result.Issues.Any(issue => issue.Field == "startup.enabled")
                        ? T("QuickSetup.Error.Startup")
                        : T("QuickSetup.Error.Generic");
                ApplyInfoBar.IsOpen = true;
                if (result.Code == "quick_setup.ai.verification_required") _aiVerified = false;
                return;
            }

            ProfileApplied?.Invoke(result.Value);
            Close();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; no further presentation update is safe or useful.
        }
        finally
        {
            _applying = false;
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                SetActionsEnabled(true);
            }
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        CompleteProfileButton.IsEnabled = enabled;
        AssistedProfileButton.IsEnabled = enabled;
        LocalRecordProfileButton.IsEnabled = enabled;
        EssentialOfflineProfileButton.IsEnabled = enabled;
        StartWithWindowsCheckBox.IsEnabled = enabled;
        PrimaryButton.IsEnabled = enabled;
        ApiSetupButton.IsEnabled = enabled;
    }

    private ToggleButton SelectedButton() => _selectedProfileId switch
    {
        QuickSetupProfileIds.Complete => CompleteProfileButton,
        QuickSetupProfileIds.Assisted => AssistedProfileButton,
        QuickSetupProfileIds.LocalRecord => LocalRecordProfileButton,
        QuickSetupProfileIds.EssentialOffline => EssentialOfflineProfileButton,
        _ => throw new InvalidOperationException("The selected Quick Setup profile is unsupported.")
    };

    private static string InferProfile(AppSettings settings) => (settings.OpenAiEnabled, settings.ScreenshotsEnabled) switch
    {
        (true, true) => QuickSetupProfileIds.Complete,
        (true, false) => QuickSetupProfileIds.Assisted,
        (false, true) => QuickSetupProfileIds.LocalRecord,
        _ => QuickSetupProfileIds.EssentialOffline
    };

    private static string ProfileTitleKey(string profileId) => profileId switch
    {
        QuickSetupProfileIds.Complete => "QuickSetup.Profile.Complete.Title",
        QuickSetupProfileIds.Assisted => "QuickSetup.Profile.Assisted.Title",
        QuickSetupProfileIds.LocalRecord => "QuickSetup.Profile.LocalRecord.Title",
        QuickSetupProfileIds.EssentialOffline => "QuickSetup.Profile.EssentialOffline.Title",
        _ => throw new InvalidOperationException("The selected Quick Setup profile is unsupported.")
    };

    private static string ProfileDescriptionKey(string profileId) => profileId switch
    {
        QuickSetupProfileIds.Complete => "QuickSetup.Profile.Complete.Description",
        QuickSetupProfileIds.Assisted => "QuickSetup.Profile.Assisted.Description",
        QuickSetupProfileIds.LocalRecord => "QuickSetup.Profile.LocalRecord.Description",
        QuickSetupProfileIds.EssentialOffline => "QuickSetup.Profile.EssentialOffline.Description",
        _ => throw new InvalidOperationException("The selected Quick Setup profile is unsupported.")
    };

    private void ConfigureWindowBehavior()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

    }

    private async void QuickSetupWindow_Closed(object sender, WindowEventArgs args)
    {
        Closed -= QuickSetupWindow_Closed;
        _lifetimeCancellation.Cancel();
        try
        {
            _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        }
        finally
        {
            _titleBar.Dispose();
            _placement.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private string T(string key) => _strings.Translate(key);

}
