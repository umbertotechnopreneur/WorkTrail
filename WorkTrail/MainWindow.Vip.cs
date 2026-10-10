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


using Microsoft.UI.Xaml;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

public sealed partial class MainWindow
{
    // sender identifies the VIP capture button.
    // e contains the click notification.
    private async void VipSnapshotButton_Click(object sender, RoutedEventArgs e) => await TakeVipSnapshotAsync();

    /// <summary>Runs the shared VIP capture flow from either the player or the tray menu.</summary>
    private async Task TakeVipSnapshotAsync()
    {
        if (_dashboardSurfaceClosed || !_workspaceUiReady || _manualScreenshotCaptureInProgress || !TakeScreenshotButton.IsEnabled) return;
        _manualScreenshotCaptureInProgress = true;
        TakeScreenshotButton.IsEnabled = false;
        var cancellationToken = _lifecycle.Token;
        var hiddenForCapture = false;
        try
        {
            if (!await _dialogs.ShowVipCountdownAsync(_application, this, _strings, cancellationToken)) return;
            cancellationToken.ThrowIfCancellationRequested();
            // Hide the player only for pixel acquisition, then restore it for the saved-capture dialog.
            _appWindow.Hide();
            hiddenForCapture = true;
            await Task.Delay(180, cancellationToken);
            var result = await _application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest("all-screens", Keep: true, ScreenshotCaptureOrigins.Manual, DeferAiAnalysis: true, IsVip: true),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _appWindow.Show();
            hiddenForCapture = false;
            Activate();
            if (!result.Succeeded || result.Value is not { StoredScreenshotPaths.Count: > 0 } capture)
            {
                await _dialogs.ShowInformativeAsync(this, DialogRequest.Informative(T("Snapshot.Vip.Take"), T(result.MessageKey), T("Dialog.Ok")));
                return;
            }
            await RefreshDashboardAsync(cancellationToken);
            await _dialogs.ShowVipSnapshotAsync(_application, this, capture, _strings);
            cancellationToken.ThrowIfCancellationRequested();
            // Continue the existing OCR/AI workflow using these pixels, never a second capture.
            await _application.AnalyzeCapturedScreenshotAsync(new(capture, KeepCapture: true, Origin: "snapshot.manual"), cancellationToken);
            await RefreshLastSessionIfDueAsync(force: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_dashboardSurfaceClosed)
            {
                if (hiddenForCapture) { _appWindow.Show(); hiddenForCapture = false; Activate(); }
                await _dialogs.ShowInformativeAsync(this, DialogRequest.Informative(T("Snapshot.Vip.Take"), T("Snapshot.Vip.Unavailable"), T("Dialog.Ok")));
            }
        }
        finally
        {
            _manualScreenshotCaptureInProgress = false;
            if (!_dashboardSurfaceClosed)
            {
                if (hiddenForCapture) { _appWindow.Show(); Activate(); }
                TakeScreenshotButton.IsEnabled = _workspaceUiReady && !_pendingSnapshotDeleteInProgress && DeleteSnapshotButton.Visibility != Visibility.Visible;
            }
        }
    }
}
