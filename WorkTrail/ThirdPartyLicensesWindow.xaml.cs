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
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.Graphics;

namespace WorkTrail;

/// <summary>Displays the runtime third-party license inventory in a themed Acrylic window.</summary>
internal sealed partial class ThirdPartyLicensesWindow : Window
{
    #region Fields

    private const int LogicalWindowWidth = 760;
    private const int LogicalWindowHeight = 650;
    private const int LogicalScreenMargin = 22;
    private readonly AppWindow _appWindow;
    private readonly AppWindow _ownerAppWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IWorkTrailApplication _application;
    private readonly LocalizationService _strings;

    #endregion

    #region Initialization

    /// <summary>Creates the passive, localized license inventory window.</summary>
    internal ThirdPartyLicensesWindow(
        IWorkTrailApplication application,
        ElementTheme theme,
        string language,
        AppWindow ownerAppWindow)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _strings = new LocalizationService(language);
        _ownerAppWindow = ownerAppWindow ?? throw new ArgumentNullException(nameof(ownerAppWindow));
        InitializeComponent();
        RootGrid.RequestedTheme = theme;
        UiLocalization.Apply(RootGrid, _strings);
        TitleText.Text = _strings.Translate("About.Licenses.Title");
        DescriptionText.Text = _strings.Translate("About.Licenses.Description");
        FavoriteMessageText.Text = _strings.Translate("About.FavoriteMessage");
        Title = _strings.Translate("About.Licenses.Title");
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleBarDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>());
        _placement = new WindowPlacementService(_application, this, _appWindow, WindowStateKeys.Licenses, LogicalWindowWidth, LogicalWindowHeight, LogicalScreenMargin, ownerAppWindow.Id);
        ConfigureWindowBehavior();
        Closed += LicensesWindow_Closed;
    }

    #endregion

    #region Implementation

    /// <summary>Gets the catalog rows rendered by the list surface.</summary>
    public IReadOnlyList<ThirdPartyLicense> LicenseRows => ThirdPartyLicenseCatalog.RuntimeDependencies;

    /// <summary>Closes the current information window.</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Opens the repository through the application link service.</summary>
    private async void RepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        RepositoryButton.IsEnabled = false;
        try
        {
            _ = await _application.OpenProductLinkAsync("repository", CancellationToken.None);
        }
        finally
        {
            RepositoryButton.IsEnabled = true;
        }
    }

    /// <summary>Restores window placement and initializes presentation after the visual tree is ready.</summary>
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, CancellationToken.None);
    }

    /// <summary>Persists placement and releases the licenses window's resources.</summary>
    private async void LicensesWindow_Closed(object sender, WindowEventArgs args)
    {
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        _titleBar.Dispose();
        _placement.Dispose();
    }

    /// <summary>Configures the information window's native presentation constraints.</summary>
    private void ConfigureWindowBehavior()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    #endregion
}
