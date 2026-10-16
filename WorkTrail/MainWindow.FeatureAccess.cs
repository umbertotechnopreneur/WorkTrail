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
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;

namespace WorkTrail;

/// <summary>
/// Coordinates the main window's feature-access state and debug-only access simulation controls.
/// </summary>
public sealed partial class MainWindow
{
    private FeatureAccessSnapshot? _featureAccessState;
#if DEBUG
    private MenuFlyoutItem? _debugFeatureProfileItem;
    private MenuFlyoutItem? _debugResetOobeItem;
    private bool _featureProfileChanging;
    private bool _debugOobeResetting;
#endif

    /// <summary>
    /// Adds debug-only feature-access commands to the main menu.
    /// </summary>
    private void InitializeFeatureAccessMenu()
    {
#if DEBUG
        _debugFeatureProfileItem = new MenuFlyoutItem();
        _debugFeatureProfileItem.Click += DebugFeatureProfile_Click;
        _debugResetOobeItem = new MenuFlyoutItem();
        _debugResetOobeItem.Click += DebugResetOobe_Click;
        MainMenuFlyout.Items.Add(new MenuFlyoutSeparator());
        MainMenuFlyout.Items.Add(_debugFeatureProfileItem);
        MainMenuFlyout.Items.Add(_debugResetOobeItem);
        UpdateDebugFeatureMenu();
#endif
    }

    /// <summary>
    /// Applies an authoritative feature-access snapshot to UI controls when the dashboard is still open.
    /// </summary>
    private void ApplyFeatureAccess(FeatureAccessSnapshot? access)
    {
        if (_dashboardSurfaceClosed) return;
        _featureAccessState = access;
        PlayerLabelFeatureGate.Access = access;
        UpdateDebugFeatureMenu();
    }

    /// <summary>
    /// Refreshes debug-only feature-access labels and enabled states.
    /// </summary>
    private void UpdateDebugFeatureMenu()
    {
#if DEBUG
        if (_debugFeatureProfileItem is null) return;
        _debugFeatureProfileItem.Text = T(_featureAccessState?.Tier == ProductTier.Premium
            ? "Premium.DebugSwitchFree" : "Premium.DebugSwitchPremium");
        _debugFeatureProfileItem.IsEnabled = !_featureProfileChanging && _featureAccessState is { CanSimulate: true };
        if (_debugResetOobeItem is not null)
        {
            _debugResetOobeItem.Text = T("Debug.ResetOobe");
            _debugResetOobeItem.IsEnabled = !_debugOobeResetting && !_dashboardSurfaceClosed;
        }
#endif
    }

    /// <summary>
    /// Retrieves feature access from the application boundary and renders its safe fallback on failure.
    /// </summary>
    private async Task RefreshFeatureAccessAsync()
    {
        try
        {
            var result = await _application.GetFeatureAccessAsync(_lifecycle.Token);
            if (_dashboardSurfaceClosed) return;
            if (!result.Succeeded || result.Value is null) throw new InvalidOperationException("Feature access could not be loaded.");
            ApplyFeatureAccess(result.Value);
        }
        catch (OperationCanceledException) when (_lifecycle.IsCancellationRequested) { }
        catch (Exception)
        {
            // A failed access lookup never unlocks controls or claims a verified license.
            ApplyFeatureAccess(null);
            if (!_dashboardSurfaceClosed)
                await _dialogs.ShowInformativeAsync(this, DialogRequest.Informative(T("Premium.Status"), T("Premium.Error"), T("Dialog.Ok")));
        }
    }

#if DEBUG
    /// <summary>
    /// Resets quick-setup completion in debug builds through the application boundary.
    /// </summary>
    private async void DebugResetOobe_Click(object sender, RoutedEventArgs e)
    {
        if (_debugOobeResetting || _dashboardSurfaceClosed) return;
        _debugOobeResetting = true;
        UpdateDebugFeatureMenu();
        try
        {
            var result = await _application.PatchSettingsAsync(
                new SettingsPatch(new Dictionary<string, string?>
                {
                    ["quick_setup.completed"] = "false"
                }),
                _lifecycle.Token);
            if (!result.Succeeded || result.Value is null)
                throw new InvalidOperationException("Debug OOBE reset failed.");
            if (_dashboardSurfaceClosed) return;

            ApplySettings(result.Value);
            DebugOobeResetRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (_lifecycle.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_dashboardSurfaceClosed)
                await _dialogs.ShowInformativeAsync(this, DialogRequest.Informative(
                    T("QuickSetup.Unavailable.Title"), T("QuickSetup.Unavailable.Message"), T("Dialog.Ok")));
        }
        finally
        {
            _debugOobeResetting = false;
            UpdateDebugFeatureMenu();
        }
    }

    /// <summary>
    /// Switches the simulated access tier in debug builds and reloads the affected settings.
    /// </summary>
    private async void DebugFeatureProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_featureProfileChanging || _featureAccessState is not { CanSimulate: true } current) return;
        _featureProfileChanging = true;
        UpdateDebugFeatureMenu();
        try
        {
            // The host serializes simulation with settings changes; the frontend never writes an entitlement flag.
            var result = await _application.SimulateFeatureAccessAsync(
                current.Tier == ProductTier.Premium ? ProductTier.Free : ProductTier.Premium, _lifecycle.Token);
            if (!result.Succeeded || result.Value is null) throw new InvalidOperationException("Debug simulation failed.");
            if (_dashboardSurfaceClosed) return;
            ApplyFeatureAccess(result.Value);
            var settings = await _application.GetSettingsAsync(_lifecycle.Token);
            if (!settings.Succeeded || settings.Value is null) throw new InvalidOperationException("Settings refresh failed.");
            if (_dashboardSurfaceClosed) return;
            _optionsControl?.ApplyExternalSettings(settings.Value);
            ApplySettings(settings.Value);
        }
        catch (OperationCanceledException) when (_lifecycle.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_dashboardSurfaceClosed)
                await _dialogs.ShowInformativeAsync(this, DialogRequest.Informative(T("Premium.Status"), T("Premium.Error"), T("Dialog.Ok")));
        }
        finally { _featureProfileChanging = false; UpdateDebugFeatureMenu(); }
    }
#endif
}
