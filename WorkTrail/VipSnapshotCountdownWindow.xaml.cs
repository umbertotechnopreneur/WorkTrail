// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Presents a cancellable VIP countdown on a native Mica surface.</summary>
internal sealed partial class VipSnapshotCountdownWindow : Window
{
    private readonly LocalizationService _strings;
    private readonly WindowPlacementService _placement;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _closed;
    private bool _captureRequested;

    /// <summary>Creates the owned countdown without acquiring any screenshot pixels.</summary>
    /// <param name="application">The facade used for window placement.</param>
    /// <param name="strings">The owner's localized strings.</param>
    /// <param name="theme">The owner's actual theme.</param>
    /// <param name="ownerAppWindow">The owner used for centering.</param>
    /// <param name="ownerHandle">The native owner handle.</param>
    internal VipSnapshotCountdownWindow(IWorkTrailApplication application, LocalizationService strings,
        ElementTheme theme, AppWindow ownerAppWindow, IntPtr ownerHandle)
    {
        InitializeComponent();
        _strings = strings;
        Title = strings.Translate("Snapshot.Vip.Take");
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = strings.Culture.Name;
        UiLocalization.Apply(RootGrid, strings);
        Badge.Text = strings.Translate("Snapshot.Vip.Badge");
        AutomationProperties.SetName(CancelButton, strings.Translate("Dialog.Cancel"));
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowHandle));
        _placement = new WindowPlacementService(application, this, appWindow,
            WindowStateKeys.VipSnapshotCountdown, 540, 500, 24, ownerAppWindow.Id);
        WindowInteropService.SetOwner(WindowHandle, ownerHandle);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _completion.TrySetResult(_captureRequested);
        };
    }

    internal IntPtr WindowHandle { get; }

    /// <summary>Shows the countdown until it finishes or is cancelled.</summary>
    /// <param name="cancellationToken">Cancels the countdown when its owner ends the operation.</param>
    internal async Task<bool> ShowAsync(CancellationToken cancellationToken)
    {
        using var cancelled = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(CloseForShutdown));
        WindowInteropService.MakeTopmostWithoutActivation(WindowHandle);
        Activate();
        return await _completion.Task;
    }

    internal void DisposePlacement() { _placement.Dispose(); _lifetime.Dispose(); }

    /// <summary>Closes this window without requesting a capture.</summary>
    internal void CloseForShutdown()
    {
        if (_closed) return;
        _captureRequested = false;
        Close();
    }

    // sender identifies the loaded countdown surface.
    // e contains the load notification.
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        var token = _lifetime.Token;
        try
        {
            _placement.ApplyDefaultBounds(RootGrid);
            await _placement.RestoreAndCenterOnOwnerAsync(RootGrid, token);
            CancelButton.Focus(FocusState.Programmatic);
            // Only the completed countdown asks the owner to run its existing capture workflow.
            for (var remaining = 5; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                SecondsText.Text = remaining.ToString(_strings.Culture);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
            token.ThrowIfCancellationRequested();
            _captureRequested = true;
            Close();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            CloseForShutdown();
            throw;
        }
    }

    // sender identifies the cancel action.
    // e contains the button click.
    private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseForShutdown();

    // sender identifies the countdown root.
    // e contains the keyboard notification.
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; CloseForShutdown(); }
    }
}
