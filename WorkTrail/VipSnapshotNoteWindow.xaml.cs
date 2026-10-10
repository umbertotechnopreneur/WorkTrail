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
using Microsoft.UI.Xaml.Input;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.System;

namespace WorkTrail;

/// <summary>Confirms a retained VIP capture and preserves the note editor on persistence failure.</summary>
internal sealed partial class VipSnapshotNoteWindow : Window
{
    private readonly IWorkTrailApplication _application;
    private readonly ScreenshotCaptureResult _capture;
    private readonly LocalizationService _strings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowPlacementService _placement;
    private bool _saving;

    /// <summary>Creates the Acrylic confirmation for a capture already marked VIP in SQLite.</summary>
    /// <param name="application">The facade for image reads and note persistence.</param>
    /// <param name="capture">The retained multi-monitor capture.</param>
    /// <param name="strings">The current localized strings.</param>
    /// <param name="theme">The owner's actual theme.</param>
    /// <param name="ownerAppWindow">The owner used for centering.</param>
    /// <param name="ownerHandle">The native owner handle.</param>
    internal VipSnapshotNoteWindow(IWorkTrailApplication application, ScreenshotCaptureResult capture,
        LocalizationService strings, ElementTheme theme, AppWindow ownerAppWindow, IntPtr ownerHandle)
    {
        InitializeComponent();
        _application = application;
        _capture = capture;
        _strings = strings;
        Title = strings.Translate("Snapshot.Vip.Saved");
        RootGrid.RequestedTheme = theme;
        RootGrid.Language = strings.Culture.Name;
        UiLocalization.Apply(RootGrid, strings);
        Badge.Text = strings.Translate("Snapshot.Vip.Badge");
        NoteBox.Header = strings.Translate("Snapshot.Vip.NotePrompt");
        AutomationProperties.SetName(NoteBox, strings.Translate("Snapshot.Vip.NotePrompt"));
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowHandle));
        _placement = new WindowPlacementService(application, this, appWindow, WindowStateKeys.VipSnapshotNote, 600, 620, 24, ownerAppWindow.Id);
        WindowInteropService.SetOwner(WindowHandle, ownerHandle);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        appWindow.Closing += (_, args) => args.Cancel = _saving;
        Preview.Configure(application, _lifetime.Token);
        Preview.SetItem(new ScreenshotGalleryItem(capture.CapturedAt ?? DateTimeOffset.Now,
            capture.StoredScreenshotPaths[0], "", "all-screens", capture.CaptureOrigin, IsVip: true),
            0, capture.StoredScreenshotPaths.Count, strings.Culture.Name);
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _completion.TrySetResult();
        };
    }

    internal IntPtr WindowHandle { get; }

    /// <summary>Shows the window until the owner or user closes it.</summary>
    internal Task ShowAsync()
    {
        WindowInteropService.MakeTopmostWithoutActivation(WindowHandle);
        Activate();
        return _completion.Task;
    }

    internal void DisposePlacement() { _placement.Dispose(); _lifetime.Dispose(); }

    /// <summary>Cancels pending note work and closes the modal window during owner shutdown.</summary>
    internal void CloseForShutdown() { _lifetime.Cancel(); _saving = false; Close(); }

    // sender identifies the newly loaded Acrylic surface.
    // e contains the load notification.
    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _placement.ApplyDefaultBounds(RootGrid);
            await _placement.RestoreAndCenterOnOwnerAsync(RootGrid, _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) NoteBox.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    // sender identifies the save-note action.
    // e contains the button click.
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;
        SaveButton.IsEnabled = ContinueButton.IsEnabled = NoteBox.IsEnabled = false;
        try
        {
            var result = await _application.SaveVipScreenshotNoteAsync(new(_capture.CaptureId, NoteBox.Text), _lifetime.Token);
            if (result.Succeeded)
            {
                _saving = false;
                Close();
                return;
            }
            Status.Message = _strings.Translate(result.MessageKey);
            Status.IsOpen = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            Status.Message = _strings.Translate("Snapshot.Vip.NoteFailed");
            Status.IsOpen = true;
        }
        finally
        {
            _saving = false;
            if (!_lifetime.IsCancellationRequested) SaveButton.IsEnabled = ContinueButton.IsEnabled = NoteBox.IsEnabled = true;
        }
    }

    // sender identifies the action that keeps the screenshot without a note.
    // e contains the button click.
    private void ContinueButton_Click(object sender, RoutedEventArgs e) => Close();

    // sender identifies the dialog root.
    // e contains the keyboard notification.
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && !_saving) { e.Handled = true; Close(); }
    }
}
