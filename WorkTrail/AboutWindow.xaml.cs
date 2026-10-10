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
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using Windows.Graphics;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Displays product information and delegates diagnostics actions to the application facade.</summary>
public sealed partial class AboutWindow : Window
{
    private const int LogicalWindowWidth = 860;
    private const int LogicalWindowHeight = 520;
    private const int LogicalScreenMargin = 22;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IWorkTrailApplication _application;
    private readonly LocalizationService _strings;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private ThirdPartyLicensesWindow? _licensesWindow;

    /// <summary>Creates and sizes the About window on the display that contains its owner.</summary>
    public AboutWindow(
        IWorkTrailApplication application,
        string theme,
        string language,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        if (ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("An owner window handle is required.", nameof(ownerHandle));
        }

        _strings = new LocalizationService(language);
        InitializeComponent();
        RootGrid.RequestedTheme = theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        UiLocalization.Apply(RootGrid, _strings);
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
            _application,
            this,
            _appWindow,
            WindowStateKeys.About,
            LogicalWindowWidth,
            LogicalWindowHeight,
            LogicalScreenMargin,
            ownerAppWindow.Id);
        WindowInteropService.SetOwner(windowHandle, ownerHandle);
        ConfigureWindowBehavior();
        _placement.ApplyDefaultBounds(RootGrid);
        Closed += AboutWindow_Closed;
    }

    /// <summary>Forwards the close interaction to the window framework.</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void AboutWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _placement.Dispose();
        _lifetimeCancellation.Cancel();
        _licensesWindow?.Close();
        _titleBar.Dispose();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
        // This fixed-size surface retains its saved position while adopting the compact content bounds.
        _placement.ResizeForContent(RootGrid, LogicalWindowWidth, LogicalWindowHeight);

        try
        {
            var result = await _application.GetProductInformationAsync(_lifetimeCancellation.Token);
            if (!result.Succeeded || result.Value is null)
            {
                throw new InvalidOperationException($"Build information is unavailable ({result.Code}).");
            }

            VersionText.Text = result.Value.Build.PackageVersion;
            BuiltAtText.Text = result.Value.Build.BuiltAtLocal.ToString("g", _strings.Culture);
            CommitText.Text = result.Value.Build.GitCommitShort;
            DirtyIndicator.Visibility = result.Value.Build.GitDirty ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; no presentation update is required.
        }
    }

    private async void ShowLogButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDiagnosticsActionAsync(
            cancellationToken => _application.OpenApplicationLogFolderAsync(cancellationToken),
            "About.LogFolderOpened");
    }

    private async void ShareLogButton_Click(object sender, RoutedEventArgs e)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
        await RunDiagnosticsActionAsync(
            cancellationToken => _application.ShareApplicationLogAsync(windowHandle, cancellationToken),
            "About.LogShared");
    }

    private async void WebsiteButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("author");

    private async void VibeWareAboutButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("vibeware");

    private async void VibeWareManifestoButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("vibeware-manifesto");

    private async void RepositoryButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("repository");

    private async void PrivacyButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("privacy");

    private async void TermsButton_Click(object sender, RoutedEventArgs e) =>
        await RunProductLinkActionAsync("terms");

    private void LicensesButton_Click(object sender, RoutedEventArgs e) => ShowLicenses();

    /// <summary>Opens or focuses the licenses work surface, including during workspace restoration.</summary>
    internal void ShowLicenses()
    {
        if (_licensesWindow is not null)
        {
            _licensesWindow.Activate();
            return;
        }

        _licensesWindow = new ThirdPartyLicensesWindow(_application, RootGrid.ActualTheme, _strings.RequestedLanguage, _appWindow);
        _licensesWindow.Closed += (_, _) => _licensesWindow = null;
        _licensesWindow.Activate();
    }

    private async Task RunProductLinkActionAsync(string linkKey)
    {
        VibeWareAboutButton.IsEnabled = false;
        VibeWareManifestoButton.IsEnabled = false;
        RepositoryButton.IsEnabled = false;
        CreatedByButton.IsEnabled = false;
        PrivacyButton.IsEnabled = false;
        TermsButton.IsEnabled = false;
        try
        {
            var result = await _application.OpenProductLinkAsync(linkKey, _lifetimeCancellation.Token);
            if (!result.Succeeded)
            {
                DiagnosticsInfoBar.Title = _strings.Translate("About.LinkFailed");
                DiagnosticsInfoBar.Message = _strings.Translate("About.LinkFailed.Description");
                DiagnosticsInfoBar.Severity = InfoBarSeverity.Error;
                DiagnosticsInfoBar.IsOpen = true;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; no presentation update is required.
        }
        catch (Exception)
        {
            DiagnosticsInfoBar.Title = _strings.Translate("About.LinkFailed");
            DiagnosticsInfoBar.Message = _strings.Translate("About.LinkFailed.Description");
            DiagnosticsInfoBar.Severity = InfoBarSeverity.Error;
            DiagnosticsInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                VibeWareAboutButton.IsEnabled = true;
                VibeWareManifestoButton.IsEnabled = true;
                RepositoryButton.IsEnabled = true;
                CreatedByButton.IsEnabled = true;
                PrivacyButton.IsEnabled = true;
                TermsButton.IsEnabled = true;
            }
        }
    }

    private async Task RunDiagnosticsActionAsync(
        Func<CancellationToken, Task<OperationResult<bool>>> action,
        string successKey)
    {
        ShowLogButton.IsEnabled = false;
        ShareLogButton.IsEnabled = false;
        try
        {
            var result = await action(_lifetimeCancellation.Token);
            DiagnosticsInfoBar.Title = _strings.Translate(result.Succeeded ? successKey : "About.LogFailed");
            DiagnosticsInfoBar.Message = result.Succeeded ? string.Empty : _strings.Translate("About.LogFailed.Description");
            DiagnosticsInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            DiagnosticsInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // The window is closing; no presentation update is required.
        }
        catch (Exception)
        {
            DiagnosticsInfoBar.Title = _strings.Translate("About.LogFailed");
            DiagnosticsInfoBar.Message = _strings.Translate("About.LogFailed.Description");
            DiagnosticsInfoBar.Severity = InfoBarSeverity.Error;
            DiagnosticsInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                ShowLogButton.IsEnabled = true;
                ShareLogButton.IsEnabled = true;
            }
        }
    }

    private void ConfigureWindowBehavior()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

    }
}
