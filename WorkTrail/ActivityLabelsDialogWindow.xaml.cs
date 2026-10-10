// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Hosts the passive label editor in a resizable, queued Mica dialog.</summary>
internal sealed partial class ActivityLabelsDialogWindow : Window
{
    private readonly TaskCompletionSource<AppSettings?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly MicaDialogService _messages = new();
    private AppSettings? _savedSettings;

    /// <summary>Creates the label-management surface using the current settings and access snapshots.</summary>
    /// <param name="application">The facade that manages labels and opens product links.</param>
    /// <param name="settings">The current label and UI settings.</param>
    /// <param name="access">The current feature-access snapshot.</param>
    /// <param name="theme">The owner's presentation theme.</param>
    /// <param name="strings">The owner's localized strings.</param>
    /// <param name="ownerAppWindow">The owning window used for placement.</param>
    /// <param name="ownerHandle">The native owning window handle.</param>
    internal ActivityLabelsDialogWindow(
        IWorkTrailApplication application,
        AppSettings settings,
        FeatureAccessSnapshot? access,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        InitializeComponent();
        Title = strings.Translate("Labels.Manage");
        RootGrid.RequestedTheme = theme;
        UiLocalization.Apply(RootGrid, strings);
        AutomationProperties.SetName(RootGrid, Title);
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowHandle));
        _titleBar = new CustomTitleBarController(
            this, appWindow, RootGrid, TitleDragRegion, TitleBarLeftInsetColumn, TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>(), useTallTitleBar: false, allowAutoHide: false);
        _placement = new WindowPlacementService(application, this, appWindow, WindowStateKeys.ActivityLabels, 560, 460, 24, ownerAppWindow.Id);
        WindowInteropService.SetOwner(WindowHandle, ownerHandle);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        }

        LabelsFeatureGate.UiLanguage = settings.UiLanguage;
        LabelsFeatureGate.Access = access;
        LabelsEditor.ApplySettings(application, settings);
        LabelsEditor.ShowUpgradeAsync = () => _messages.ShowPremiumUpgradeAsync(
            application, this, strings.Translate, "Labels.FreeLimit");
        LabelsEditor.SettingsSaved += saved => _savedSettings = saved;
        LabelsEditor.BusyChanged += busy => CloseButton.IsEnabled = !busy;
        appWindow.Closing += (_, args) =>
        {
            if (LabelsEditor.IsBusy) args.Cancel = true;
        };
        Closed += (_, _) =>
        {
            _messages.CloseActive();
            _titleBar.Dispose();
            _completion.TrySetResult(_savedSettings);
        };
    }

    internal IntPtr WindowHandle { get; }

    /// <summary>Shows the modal editor and returns the last successfully saved snapshot after closure.</summary>
    internal Task<AppSettings?> ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(WindowHandle);
        Activate();
        return _completion.Task;
    }

    internal void DisposePlacement() => _placement.Dispose();

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreAndCenterOnOwnerAsync(RootGrid, CancellationToken.None);
        if (LabelsFeatureGate.Access is { } access && FeatureCatalog.IsAllowed(ProductFeature.ActivityLabels, access))
            LabelsEditor.FocusEditor();
        else
            CloseButton.Focus(FocusState.Programmatic);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || LabelsEditor.IsBusy) return;
        e.Handled = true;
        Close();
    }
}
