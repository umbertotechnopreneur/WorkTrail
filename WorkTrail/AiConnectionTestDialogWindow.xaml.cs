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


using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Displays the bounded, topmost progress and result surface for an explicit AI connection check.</summary>
internal sealed partial class AiConnectionTestDialogWindow : Window
{
    private const int LogicalWidth = 560;
    private const int LogicalHeight = 480;
    private const int LogicalScreenMargin = 24;
    private const int TerminalChunkSize = 24;
    private const int MaxTerminalOutputCharacters = 4096;
    private static readonly TimeSpan TerminalUpdateDelay = TimeSpan.FromMilliseconds(14);
    private readonly IWorkTrailApplication _application;
    private readonly LocalizationService _strings;
    private readonly AppWindow _appWindow;
    private readonly CustomTitleBarController _titleBar;
    private readonly WindowPlacementService _placement;
    private readonly IntPtr _windowHandle;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _testCancellation = new(TimeSpan.FromSeconds(30));
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherQueueTimer _countdownTimer;
    private readonly DateTimeOffset _deadline = DateTimeOffset.Now.AddSeconds(30);
    private readonly StringBuilder _terminalBuffer = new();
    private bool _isClosing;

    /// <summary>Creates the passive acrylic test surface using the shared application facade.</summary>
    internal AiConnectionTestDialogWindow(
        IWorkTrailApplication application,
        ElementTheme theme,
        AppWindow ownerAppWindow,
        IntPtr ownerHandle,
        LocalizationService strings)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        ArgumentNullException.ThrowIfNull(ownerAppWindow);
        InitializeComponent();
        RootGrid.RequestedTheme = theme;
        UiLocalization.Apply(RootGrid, _strings);
        Title = T("AiConnectionTest.WindowTitle");
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _titleBar = new CustomTitleBarController(
            this,
            _appWindow,
            RootGrid,
            TitleDragRegion,
            TitleBarLeftInsetColumn,
            TitleBarRightInsetColumn,
            static () => Array.Empty<FrameworkElement>());
        _placement = new WindowPlacementService(application, this, _appWindow, WindowStateKeys.AiConnectionTest, LogicalWidth, LogicalHeight, LogicalScreenMargin, ownerAppWindow.Id);
        WindowInteropService.SetOwner(_windowHandle, ownerHandle);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        _countdownTimer = DispatcherQueue.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromMilliseconds(200);
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        Closed += (_, _) =>
        {
            _isClosing = true;
            _testCancellation.Cancel();
            _lifetimeCancellation.Cancel();
            _countdownTimer.Stop();
            _titleBar.Dispose();
            _testCancellation.Dispose();
            _lifetimeCancellation.Dispose();
            _completion.TrySetResult();
        };
    }

    /// <summary>Activates the topmost dialog and completes after the user dismisses it.</summary>
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
        try
        {
            _placement.ApplyDefaultBounds(RootGrid);
            await _placement.RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token);
            _countdownTimer.Start();
            await RunTestAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Closing during initial placement cancels the remaining presentation work.
        }
    }

    private async Task RunTestAsync()
    {
        try
        {
            var requestTask = _application.TestAiConnectionAsync(_testCancellation.Token);
            await AppendTerminalAsync($"$ {T("AiConnectionTest.Terminal.Prompt")}{Environment.NewLine}{AiConnectionTestProtocol.Prompt}", _testCancellation.Token);
            TerminalStateText.Text = T("AiConnectionTest.State.Waiting");
            var result = await requestTask;
            if (_isClosing)
            {
                return;
            }

            if (result.Succeeded && result.Value is not null)
            {
                CompleteTest(success: true);
                TitleText.Text = T("AiConnectionTest.Connected.Title");
                StatusText.Text = string.Format(
                    _strings.Culture,
                    "{0} · {1} · {2:N0} ms",
                    ProviderDisplayName(result.Value.Provider),
                    result.Value.Model,
                    result.Value.ElapsedMilliseconds);
                TerminalStateText.Text = T("AiConnectionTest.State.Response");
                var output = string.IsNullOrWhiteSpace(result.Value.Output)
                    ? T("AiConnectionTest.EmptyResponse")
                    : BoundTerminalOutput(result.Value.Output.Trim());
                await AppendTerminalAsync(
                    $"{Environment.NewLine}{Environment.NewLine}> {T("AiConnectionTest.Terminal.Response")}{Environment.NewLine}{output}",
                    _lifetimeCancellation.Token);
                return;
            }

            CompleteTest(success: false);
            TitleText.Text = T("AiConnectionTest.Failed.Title");
            StatusText.Text = result.Code == "ai.connection.key.missing"
                ? T("AiConnectionTest.Failed.MissingKey")
                : result.Code == "ai.connection.configuration.invalid"
                    ? T("AiConnectionTest.Failed.InvalidConfiguration")
                    : T("AiConnectionTest.Failed.Generic");
            TerminalStateText.Text = T("AiConnectionTest.State.Error");
            await AppendTerminalAsync(
                $"{Environment.NewLine}{Environment.NewLine}! {T("AiConnectionTest.Terminal.Error")}{Environment.NewLine}{StatusText.Text}",
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_isClosing)
        {
            // Closing the surface cancels both the provider request and the presentation-only teletype animation.
        }
        catch (OperationCanceledException) when (!_isClosing)
        {
            CompleteTest(success: false);
            TitleText.Text = T("AiConnectionTest.Timeout.Title");
            StatusText.Text = T("AiConnectionTest.Timeout.Message");
            TerminalStateText.Text = T("AiConnectionTest.State.Timeout");
            await AppendTerminalAsync(
                $"{Environment.NewLine}{Environment.NewLine}! {T("AiConnectionTest.Terminal.Timeout")}{Environment.NewLine}{StatusText.Text}",
                _lifetimeCancellation.Token);
        }
    }

    private void UpdateCountdown()
    {
        var remaining = Math.Max(0, (int)Math.Ceiling((_deadline - DateTimeOffset.Now).TotalSeconds));
        CountdownText.Text = $"00:{remaining:00}";
    }

    private void CompleteTest(bool success)
    {
        _countdownTimer.Stop();
        Progress.IsActive = false;
        Progress.Visibility = Visibility.Collapsed;
        CountdownPanel.Visibility = Visibility.Collapsed;
        ResultIcon.Glyph = success ? "\uE73E" : "\uEA39";
        ResultIcon.Foreground = new SolidColorBrush(success ? Colors.ForestGreen : Colors.IndianRed);
        ResultIcon.Visibility = Visibility.Visible;
        CloseButton.Content = T(success ? "Dialog.Ok" : "AiConnectionTest.Close");
    }

    private async Task AppendTerminalAsync(string text, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < text.Length; offset += TerminalChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(TerminalChunkSize, text.Length - offset);
            _terminalBuffer.Append(text, offset, count);
            TerminalText.Text = _terminalBuffer.ToString() + "▋";
            TerminalScrollViewer.ChangeView(null, TerminalScrollViewer.ScrollableHeight, null, disableAnimation: true);
            await Task.Delay(TerminalUpdateDelay, cancellationToken);
        }
    }

    private static string BoundTerminalOutput(string output) => output.Length <= MaxTerminalOutputCharacters
        ? output
        : output[..MaxTerminalOutputCharacters] + Environment.NewLine + "…";

    private static string ProviderDisplayName(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "openai" or "open-ai" => "OpenAI",
        "openrouter" => "OpenRouter",
        "anthropic" => "Anthropic",
        _ => provider
    };

    private string T(string key) => _strings.Translate(key);

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        _testCancellation.Cancel();
        _lifetimeCancellation.Cancel();
        CloseButton.IsEnabled = false;
        _ = await _placement.TrySaveForCloseAsync(CancellationToken.None);
        Close();
    }

}
