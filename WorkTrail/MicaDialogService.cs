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
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail;

/// <summary>Serializes standard WinUI dialogs and dedicated rich windows while keeping views passive.</summary>
internal sealed class MicaDialogService
{
    private readonly DialogSessionQueue _queue = new();
    private Window? _activeWindow;
    private ContentDialog? _activeContentDialog;

    /// <summary>Gets the service that shows and hides reusable toast UI components.</summary>
    internal ToastNotificationService Notifications { get; } = new();

    /// <summary>Offers explicit exit, notification-area hide, or cancellation with a safe keyboard default.</summary>
    internal async Task<ContentDialogResult> ChooseLauncherCloseAsync(Window owner, Func<string, string> translate)
    {
        var request = new DialogRequest(translate("Launcher.CloseTitle"), translate("Launcher.CloseMessage"),
            translate("Launcher.CloseToTray"), translate("Dialog.Cancel"), translate("Launcher.Exit"));
        return await RunContentDialogSessionAsync(owner, request, ContentDialogButton.Primary);
    }

    /// <summary>Shows one queued standard WinUI acknowledgement dialog.</summary>
    internal async Task ShowInformativeAsync(Window owner, DialogRequest request)
    {
        ValidateDialogRequest(request, requiresCloseButton: false);
        _ = await RunContentDialogSessionAsync(owner, request, ContentDialogButton.Primary);
    }

    /// <summary>Shows the shared Premium prompt and opens the product's Microsoft Store page on request.</summary>
    /// <param name="application">The facade that opens the allowlisted Store link.</param>
    /// <param name="owner">The window that owns the queued dialog.</param>
    /// <param name="translate">The owner's current localized strings.</param>
    /// <param name="messageKey">The explanation for the blocked action.</param>
    /// <exception cref="ArgumentNullException">A required facade, owner, or translator is missing.</exception>
    /// <exception cref="InvalidOperationException">The owner's UI thread or loaded content is unavailable.</exception>
    internal async Task ShowPremiumUpgradeAsync(
        IWorkTrailApplication application,
        Window owner,
        Func<string, string> translate,
        string messageKey = "Premium.Required")
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(translate);
        ValidateOwnerThread(owner);
        using var lifetime = new CancellationTokenSource();
        // Keep the launch tied to the owner even after the dialog releases its queue lease.
        // sender identifies the owner window.
        // args contains the window-closed notification.
        void OwnerClosed(object sender, WindowEventArgs args) => lifetime.Cancel();
        owner.Closed += OwnerClosed;
        try
        {
            var content = new StackPanel { Spacing = 16, MinWidth = 280 };
            content.Children.Add(new Controls.PremiumBadge
            {
                Text = translate("Premium.Badge"),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            content.Children.Add(new TextBlock
            {
                Text = translate(messageKey),
                TextWrapping = TextWrapping.Wrap
            });
            var storeCaption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            storeCaption.Children.Add(new FontIcon { Glyph = "\uE719", FontSize = 18 });
            storeCaption.Children.Add(new TextBlock
            {
                Text = translate("Premium.StoreAction"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(storeCaption);
            var request = DialogRequest.Confirmation(
                translate("Premium.UpgradeTitle"), translate(messageKey),
                translate("Premium.UpgradeTitle"), translate("Window.Close"));
            // Highlight the Store action; opening its page does not authorize a purchase.
            var choice = await RunContentDialogSessionAsync(owner, request, ContentDialogButton.Primary, content: content);
            if (choice != ContentDialogResult.Primary || lifetime.IsCancellationRequested)
            {
                return;
            }

            var result = await application.OpenProductLinkAsync("store", lifetime.Token);
            if (!result.Succeeded && !lifetime.IsCancellationRequested)
            {
                await ShowInformativeAsync(owner, DialogRequest.Informative(
                    translate("Premium.UpgradeTitle"), translate("Premium.StoreUnavailable"), translate("Dialog.Ok")));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Closing the owner cancels the purchase prompt without launching the Store.
        }
        finally
        {
            owner.Closed -= OwnerClosed;
        }
    }

    /// <summary>Shows one queued standard WinUI OK/Cancel confirmation.</summary>
    /// <returns><see langword="true"/> only when the user explicitly chooses OK; dismissal safely cancels.</returns>
    internal async Task<bool> ConfirmAsync(Window owner, DialogRequest request)
    {
        ValidateDialogRequest(request, requiresCloseButton: true);
        return await RunContentDialogSessionAsync(owner, request, ContentDialogButton.Close) == ContentDialogResult.Primary;
    }

    /// <summary>Shows a queued confirmation with caller-provided WinUI content.</summary>
    /// <param name="owner">The window that owns the dialog.</param>
    /// <param name="request">The localized title and button labels.</param>
    /// <param name="content">The UI element displayed inside the dialog.</param>
    /// <returns><see langword="true"/> only when the user explicitly chooses the primary action.</returns>
    internal async Task<bool> ConfirmAsync(Window owner, DialogRequest request, object content)
    {
        ValidateDialogRequest(request, requiresCloseButton: true);
        ArgumentNullException.ThrowIfNull(content);
        return await RunContentDialogSessionAsync(owner, request, ContentDialogButton.Close, content: content) == ContentDialogResult.Primary;
    }

    /// <summary>Shows the searchable world-clock catalog and reports whether it added at least one city.</summary>
    internal async Task<bool> ShowWorldClockCityPickerAsync(
        IWorkTrailApplication application,
        Window owner,
        IReadOnlyList<WorldClockCitySummary> cities,
        ElementTheme theme,
        LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(strings);
        return await RunModalSessionAsync(owner, false, async (ownerAppWindow, ownerHandle) =>
        {
            var dialog = new WorldClockCityPickerDialogWindow(
                application,
                cities,
                theme,
                strings,
                ownerAppWindow,
                ownerHandle,
                Notifications);
            return await ShowDialogWindowAsync(
                dialog,
                dialog.WindowHandle,
                dialog.ShowAsync,
                dialog.DisposePlacement);
        });
    }

    /// <summary>Collects the first home city with the reusable offline selector and the Windows time-zone suggestion.</summary>
    internal async Task<bool> ShowWelcomeAsync(IWorkTrailApplication application, Window owner,
        WorldClockCityCatalog catalog, LocalizationService strings)
    {
        return await RunModalSessionAsync(owner, false, async (ownerAppWindow, ownerHandle) =>
        {
            var dialog = new WorldClockCityPickerDialogWindow(application, catalog.Cities,
                ((FrameworkElement)owner.Content).ActualTheme, strings, ownerAppWindow, ownerHandle, Notifications,
                welcome: true, catalog.SuggestedCityId, catalog.WindowsTimeZoneName);
            return await ShowDialogWindowAsync(dialog, dialog.WindowHandle, dialog.ShowAsync, dialog.DisposePlacement);
        });
    }

    /// <summary>Cancels queued requests and closes active WinUI dialogs, rich windows, and toasts during shutdown.</summary>
    internal void CloseActive()
    {
        _queue.Shutdown();
        _activeContentDialog?.Hide();
        CloseActiveWindow();
        Notifications.HideAll();
    }

    private void CloseActiveWindow()
    {
        switch (_activeWindow)
        {
            case WorldClockCityPickerDialogWindow pickerWindow:
                pickerWindow.CloseForShutdown();
                break;
            default:
                _activeWindow?.Close();
                break;
        }
    }

    // owner identifies the window that owns this queued dialog.
    // request contains the localized content and button labels.
    // defaultButton identifies the safe keyboard default.
    // content supplies optional custom dialog content.
    private async Task<ContentDialogResult> RunContentDialogSessionAsync(
        Window owner,
        DialogRequest request,
        ContentDialogButton defaultButton,
        object? content = null)
    {
        ValidateOwnerThread(owner);
        using var ownerLifetime = new CancellationTokenSource();
        ContentDialog? dialog = null;
        void OwnerClosed(object sender, WindowEventArgs args)
        {
            ownerLifetime.Cancel();
            dialog?.Hide();
        }

        owner.Closed += OwnerClosed;
        try
        {
            using var session = await _queue.EnterAsync(ownerLifetime.Token);
            if (session is null || _queue.IsShuttingDown || ownerLifetime.IsCancellationRequested)
            {
                return ContentDialogResult.None;
            }

            ValidateOwnerThread(owner);
            if (owner.Content is not FrameworkElement { XamlRoot: { } xamlRoot } ownerContent)
            {
                // A standard ContentDialog has no valid visual root before its owner content is loaded.
                throw new InvalidOperationException("Content dialogs require a loaded owner XamlRoot.");
            }

            dialog = CreateContentDialog(xamlRoot, ownerContent, request, defaultButton, content);
            _activeContentDialog = dialog;
            try
            {
                // ContentDialog is an overlay: restore a hidden tray owner so the request is reachable.
                owner.Activate();
                var result = await dialog.ShowAsync();
                return _queue.IsShuttingDown || ownerLifetime.IsCancellationRequested
                    ? ContentDialogResult.None
                    : result;
            }
            finally
            {
                _activeContentDialog = null;
                dialog = null;
            }
        }
        finally
        {
            owner.Closed -= OwnerClosed;
        }
    }

    private async Task RunModalSessionAsync(
        Window owner,
        Func<AppWindow, IntPtr, Task> showAsync)
    {
        ArgumentNullException.ThrowIfNull(showAsync);
        _ = await RunModalSessionAsync(owner, false, async (ownerAppWindow, ownerHandle) =>
        {
            await showAsync(ownerAppWindow, ownerHandle);
            return true;
        });
    }

    private async Task<TResult> RunModalSessionAsync<TResult>(
        Window owner,
        TResult shutdownResult,
        Func<AppWindow, IntPtr, Task<TResult>> showAsync)
    {
        ValidateOwnerThread(owner);
        ArgumentNullException.ThrowIfNull(showAsync);
        using var ownerLifetime = new CancellationTokenSource();
        var presenting = false;
        void OwnerClosed(object sender, WindowEventArgs args)
        {
            ownerLifetime.Cancel();
            if (presenting)
            {
                CloseActiveWindow();
            }
        }

        owner.Closed += OwnerClosed;
        try
        {
            using var session = await _queue.EnterAsync(ownerLifetime.Token);
            if (session is null || _queue.IsShuttingDown || ownerLifetime.IsCancellationRequested)
            {
                return shutdownResult;
            }

            ValidateOwnerThread(owner);
            var ownerContent = owner.Content as UIElement;
            var ownerWasInteractive = ownerContent?.IsHitTestVisible ?? false;
            presenting = true;
            try
            {
                if (ownerContent is not null)
                {
                    ownerContent.IsHitTestVisible = false;
                }

                var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
                var ownerWindowId = Win32Interop.GetWindowIdFromWindow(ownerHandle);
                var ownerAppWindow = AppWindow.GetFromWindowId(ownerWindowId);
                var result = await showAsync(ownerAppWindow, ownerHandle);
                return _queue.IsShuttingDown || ownerLifetime.IsCancellationRequested ? shutdownResult : result;
            }
            finally
            {
                presenting = false;
                if (!ownerLifetime.IsCancellationRequested && ownerContent is not null)
                {
                    ownerContent.IsHitTestVisible = ownerWasInteractive;
                }
            }
        }
        finally
        {
            owner.Closed -= OwnerClosed;
        }
    }

    private async Task ShowDialogWindowAsync(
        Window dialog,
        IntPtr dialogHandle,
        Func<Task> showAsync,
        Action disposePlacement)
    {
        _ = await ShowDialogWindowAsync(dialog, dialogHandle, async () =>
        {
            await showAsync();
            return true;
        }, disposePlacement);
    }

    private async Task<TResult> ShowDialogWindowAsync<TResult>(
        Window dialog,
        IntPtr dialogHandle,
        Func<Task<TResult>> showAsync,
        Action disposePlacement)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(showAsync);
        ArgumentNullException.ThrowIfNull(disposePlacement);
        IReadOnlyList<IntPtr>? disabledPeerWindows = null;
        try
        {
            _activeWindow = dialog;
            disabledPeerWindows = WindowInteropService.DisableCurrentThreadPeerWindows(dialogHandle);
            return await showAsync();
        }
        finally
        {
            _activeWindow = null;
            try
            {
                disposePlacement();
            }
            finally
            {
                if (disabledPeerWindows is not null)
                {
                    WindowInteropService.RestoreWindows(disabledPeerWindows);
                }
            }
        }
    }

    private static ContentDialog CreateContentDialog(
        XamlRoot xamlRoot,
        FrameworkElement ownerContent,
        DialogRequest request,
        ContentDialogButton defaultButton,
        object? content = null) =>
        new()
        {
            XamlRoot = xamlRoot,
            RequestedTheme = ownerContent.ActualTheme,
            Language = ownerContent.Language,
            Title = request.Title,
            Content = content ?? request.Message,
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = request.CloseButtonText,
            SecondaryButtonText = request.SecondaryButtonText,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["WorkTrailDialogStyle"],
            DefaultButton = defaultButton
        };

    private static void ValidateOwnerThread(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.DispatcherQueue.HasThreadAccess)
        {
            // Dialog windows share their owner's dispatcher; no background-thread UI fallback is supported.
            throw new InvalidOperationException("Dialogs must be controlled from their owner UI thread.");
        }
    }

    private static void ValidateDialogRequest(DialogRequest request, bool requiresCloseButton)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Message) ||
            string.IsNullOrWhiteSpace(request.PrimaryButtonText) ||
            (requiresCloseButton && string.IsNullOrWhiteSpace(request.CloseButtonText)))
        {
            throw new ArgumentException("Dialog title, message, and required button labels must be supplied.", nameof(request));
        }
    }

}
