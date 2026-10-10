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
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Shows simplified AI pricing and locally estimated OpenAI costs in a queued acrylic dialog.</summary>
internal sealed partial class AiPricingDialogWindow : Window
{
    private const int LogicalWidth = 680;
    private const int LogicalHeight = 590;
    private const int LogicalScreenMargin = 24;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AiPricingOverview _overview;
    private readonly LocalizationService _strings;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IntPtr _windowHandle;
    private bool _isCompleting;

    /// <summary>Creates a passive, localized pricing dialog from application-layer data.</summary>
    internal AiPricingDialogWindow(
        IWorkTrailApplication application,
        AiPricingOverview overview,
        ElementTheme theme,
        LocalizationService strings,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle)
    {
        ArgumentNullException.ThrowIfNull(application);
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        InitializeComponent();
        Title = T("AiPricing.Title");
        RootGrid.RequestedTheme = theme;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>(),
            useTallTitleBar: false);
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.AiPricing, LogicalWidth, LogicalHeight, LogicalScreenMargin, ownerAppWindow.Id);
        WindowInteropService.SetOwner(_windowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        ApplyContent();
        Closed += (_, _) =>
        {
            _titleBar.Dispose();
            _completion.TrySetResult();
        };
    }

    /// <summary>Activates the detached acrylic surface and completes after closure.</summary>
    internal Task ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(_windowHandle);
        Activate();
        return _completion.Task;
    }

    internal IntPtr WindowHandle => _windowHandle;

    internal void DisposePlacement() => _placement.Dispose();

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _placement.ApplyDefaultBounds(RootGrid);
        await _placement.RestoreOrCenterAsync(RootGrid, CancellationToken.None);
        CloseButton.Focus(FocusState.Programmatic);
    }

    private void ApplyContent()
    {
        UiLocalization.Apply(RootGrid, _strings);
        var culture = _strings.Culture;
        DialogTitleText.Text = T("AiPricing.Title");
        DialogSubtitleText.Text = T("AiPricing.Subtitle");
        AutomationProperties.SetName(RootGrid, DialogTitleText.Text);
        AutomationProperties.SetName(DialogTitleText, DialogTitleText.Text);
        AutomationProperties.SetName(DialogSubtitleText, DialogSubtitleText.Text);

        EstimatedTodayLabel.Text = T("AiPricing.EstimatedToday");
        EstimatedTodayValue.Text = FormatOptionalUsd(_overview.EstimatedCostTodayUsd, T("AiPricing.Unavailable"), decimals: 4);
        EstimatedRequestsLabel.Text = T("AiPricing.EstimatedRequests");
        EstimatedRequestsValue.Text = _overview.EstimatedCostTodayRequestCount.ToString("N0", culture);
        TodayTokensLabel.Text = T("AiPricing.TodayTokens");
        TodayTokensValue.Text = _overview.TodayTotalTokens.ToString("N0", culture);
        LastSyncLabel.Text = T("AiPricing.LastSync");
        LastSyncValue.Text = _overview.LastSynchronizedAt is { } synchronizedAt
            ? synchronizedAt.ToLocalTime().ToString("g", culture)
            : T("AiPricing.NeverSynced");
        DisplayedModelsLabel.Text = T("AiPricing.Models");
        DisplayedModelsValue.Text = _overview.DisplayedModelCount.ToString("N0", culture);
        ReportedCostLabel.Text = T("AiPricing.ReportedCost");
        ReportedCostValue.Text = FormatOptionalUsd(_overview.ActualCostTodayUsd, T("AiPricing.Unavailable"), decimals: 4);

        ModelHeaderText.Text = T("AiPricing.Table.Model");
        InputHeaderText.Text = T("AiPricing.Table.Input");
        OutputHeaderText.Text = T("AiPricing.Table.Output");
        CloseButton.Content = T("About.Close");
        AutomationProperties.SetName(CloseButton, T("About.Close"));
        PopulatePricingRows();
    }

    private void PopulatePricingRows()
    {
        PricingRowsPanel.Children.Clear();
        if (_overview.Models.Count == 0)
        {
            PricingRowsPanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(16),
                Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
                IsTextSelectionEnabled = true,
                Text = T("AiPricing.NoPrices"),
                TextWrapping = TextWrapping.WrapWholeWords
            });
            return;
        }

        foreach (var row in _overview.Models)
        {
            PricingRowsPanel.Children.Add(CreatePricingRow(row));
        }
    }

    private UIElement CreatePricingRow(AiPricingCostRow row)
    {
        var container = new Border
        {
            MinHeight = 42,
            Padding = new Thickness(16, 0, 16, 0),
            BorderBrush = ResourceBrush("DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var grid = new Grid
        {
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var model = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            IsTextSelectionEnabled = true,
            Text = row.Model,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(model);

        var input = CreatePriceCell(FormatUsd(row.InputUsdPerMillionTokens, decimals: 4));
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);

        var output = CreatePriceCell(FormatUsd(row.OutputUsdPerMillionTokens, decimals: 4));
        Grid.SetColumn(output, 2);
        grid.Children.Add(output);

        container.Child = grid;
        return container;
    }

    private static TextBlock CreatePriceCell(string value) => new()
    {
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Right,
        IsTextSelectionEnabled = true,
        Margin = new Thickness(0, 0, 6, 0),
        Text = value,
        TextAlignment = TextAlignment.Right,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private string T(string key) => _strings.Translate(key);

    private string FormatOptionalUsd(decimal? value, string unavailable, int decimals) =>
        value.HasValue ? FormatUsd(value.Value, decimals) : unavailable;

    private string FormatUsd(decimal value, int decimals)
    {
        return _strings.Format(
            decimals <= 2 ? "AiPricing.UsdShort" : "AiPricing.UsdDetailed",
            value);
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await CompleteAsync();
    }

    private async void RootGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        await CompleteAsync();
    }

    private async Task CompleteAsync()
    {
        if (_isCompleting)
        {
            return;
        }

        _isCompleting = true;
        CloseButton.IsEnabled = false;
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        Close();
    }

    private static Brush ResourceBrush(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Transparent);

}
