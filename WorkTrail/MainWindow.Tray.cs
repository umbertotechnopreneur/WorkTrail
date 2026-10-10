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


using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail;

public sealed partial class MainWindow
{
    private bool _notificationsEnabled = true;
    private bool _trayCommandInProgress;

    /// <summary>Requests one existing astronomical calendar, day/night map, or globe surface.</summary>
    public event Action<TrayIconMenuCommand>? TrayAstronomyRequested;

    /// <summary>Reads only cached UI state so opening the native popup never performs facade work.</summary>
    private TrayIconMenuState CreateTrayMenuState() => new(
        T("Tray.ShowAllWindows"),
        T(_notificationsEnabled ? "Tray.PauseNotifications" : "Tray.EnableNotifications"),
        T("Snapshot.Vip.Take"),
        T(_isTracking ? "TrackingActionPause" : "Tray.ResumeTracking"),
        T("Tray.WorldClocks"),
        T("Tray.AstronomicalCalendar"),
        T("Tray.DayNightMap"),
        T("Tray.DayNightGlobe"),
        T("MenuTitleOptions"),
        T("MenuTitleAbout"),
        _workspaceUiReady && !_dashboardSurfaceClosed && !_trayCommandInProgress,
        _workspaceUiReady && !_dashboardSurfaceClosed && !_trayCommandInProgress
            && !_manualScreenshotCaptureInProgress && TakeScreenshotButton.IsEnabled);

    // command identifies the action selected after the native popup has returned.
    private void TrayIcon_CommandRequested(TrayIconMenuCommand command)
    {
        // Never open WinUI windows or await runtime work inside the Win32 subclass callback.
        _ = DispatcherQueue.TryEnqueue(async () => await HandleTrayCommandAsync(command));
    }

    // command identifies the existing action selected in the tray context menu.
    private async Task HandleTrayCommandAsync(TrayIconMenuCommand command)
    {
        if (!_workspaceUiReady || _dashboardSurfaceClosed || _trayCommandInProgress) return;
        _trayCommandInProgress = true;
        try
        {
            switch (command)
            {
                case TrayIconMenuCommand.ShowAllWindows:
                    await RevealOpenWindowsAsync(showMainWindow: true);
                    break;
                case TrayIconMenuCommand.ToggleNotifications:
                    var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?>
                    {
                        ["notifications.enabled"] = _notificationsEnabled ? "false" : "true"
                    }), _lifecycle.Token);
                    if (!result.Succeeded || result.Value is null)
                    {
                        ShowFlyout();
                        await _dialogs.ShowInformativeAsync(this,
                            DialogRequest.Informative("WorkTrail", T(result.MessageKey), T("Dialog.Ok")));
                        return;
                    }
                    await ApplyExternalSettingsAsync(result.Value);
                    break;
                case TrayIconMenuCommand.TakeVipSnapshot:
                    ShowFlyout();
                    await TakeVipSnapshotAsync();
                    break;
                case TrayIconMenuCommand.ToggleTracking:
                    if (!await ToggleTrackingAsync())
                    {
                        ShowFlyout();
                        await ShowLazySurfaceFailureAsync();
                    }
                    break;
                case TrayIconMenuCommand.WorldClocks:
                    WorldClocksRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case TrayIconMenuCommand.AstronomicalCalendar:
                case TrayIconMenuCommand.DayNightMap:
                case TrayIconMenuCommand.DayNightGlobe:
                    TrayAstronomyRequested?.Invoke(command);
                    break;
                case TrayIconMenuCommand.Settings:
                    ShowFlyout();
                    await ShowOptionsPanelAsync();
                    break;
                case TrayIconMenuCommand.About:
                    ShowFlyout();
                    ShowAboutWindow();
                    break;
            }
        }
        catch (OperationCanceledException) when (_lifecycle.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_dashboardSurfaceClosed)
            {
                ShowFlyout();
                ApplicationErrorService.Report(exception, "TrayMenu", _strings.RequestedLanguage);
            }
        }
        finally { _trayCommandInProgress = false; }
    }
}
