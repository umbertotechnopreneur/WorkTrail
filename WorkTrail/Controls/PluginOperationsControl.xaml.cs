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


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects and renders local context-plugin operations.</summary>
public sealed partial class PluginOperationsControl : UserControl
{
    private static readonly HashSet<string> BuiltInPluginIds = new(StringComparer.Ordinal)
    {
        "word",
        "excel",
        "vscode",
        "browser"
    };

    private LocalizationService _strings = new("system");
    private OperationsSectionContext? _context;
    private PluginInfo[] _plugins = [];
    private bool _isApplyingPluginState;

    /// <summary>Creates the independent plugin operations surface.</summary>
    public PluginOperationsControl() => InitializeComponent();

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        AutomationProperties.SetName(PluginsList, _strings.Translate("Operations.Plugins"));
        ApplyPlugins(_plugins);
    }

    internal void Initialize(IWorkTrailApplication application, MicaDialogService dialogs, Window ownerWindow, TimedInfoBar banner) =>
        _context = new OperationsSectionContext(
            application,
            dialogs,
            ownerWindow,
            banner,
            active =>
            {
                Progress.IsActive = active;
                Progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            },
            SectionBody,
            key => _strings.TryTranslate(key, out var value) ? value : null);

    private OperationsSectionContext Context => _context ?? throw new InvalidOperationException("PluginOperationsControl must be initialized before use.");

    internal async Task LoadAsync()
    {
        var result = await Context.ExecuteAsync((application, token) => application.GetPluginsAsync(token), showSuccess: false);
        if (result is { Succeeded: true, Value: { } plugins })
        {
            ApplyPlugins(plugins.ToArray());
        }
    }

    private async void PluginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingPluginState || sender is not ToggleSwitch toggle || toggle.Tag is not string pluginId)
        {
            return;
        }

        var plugin = _plugins.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id, pluginId));
        if (plugin is null || plugin.Enabled == toggle.IsOn)
        {
            // Initial binding and programmatic restoration can raise Toggled; neither is a user mutation.
            return;
        }

        var requestedState = toggle.IsOn;
        var result = await Context.ExecuteAsync(
            (application, token) => application.SetPluginEnabledAsync(plugin.Id, requestedState, token),
            showSuccess: false);
        if (result is { Succeeded: true, Value: { } updatedPlugin } &&
            StringComparer.Ordinal.Equals(updatedPlugin.Id, plugin.Id) &&
            updatedPlugin.Enabled == requestedState)
        {
            ReplacePlugin(updatedPlugin);
            Context.ShowStatus(
                _strings.Translate("Operations.Status.Completed.Title"),
                Context.ResultMessage(result.MessageKey, succeeded: true),
                InfoBarSeverity.Success);
            return;
        }

        RestoreToggle(toggle, plugin.Enabled);
        if (result is { Succeeded: true })
        {
            Context.ShowStatus(
                _strings.Translate("Operations.Status.Failed.Title"),
                _strings.Translate("Operations.Plugin.InvalidState"),
                InfoBarSeverity.Error);
        }
    }

    private void ApplyPlugins(PluginInfo[] plugins)
    {
        _isApplyingPluginState = true;
        try
        {
            _plugins = plugins;
            PluginsList.ItemsSource = plugins.Select(CreateListItem).ToArray();
        }
        finally
        {
            _isApplyingPluginState = false;
        }
    }

    private void ReplacePlugin(PluginInfo updatedPlugin)
    {
        var index = Array.FindIndex(_plugins, plugin => StringComparer.Ordinal.Equals(plugin.Id, updatedPlugin.Id));
        if (index < 0)
        {
            throw new InvalidOperationException("The updated plugin is not present in the loaded plugin collection.");
        }

        var updatedPlugins = (PluginInfo[])_plugins.Clone();
        updatedPlugins[index] = updatedPlugin;
        ApplyPlugins(updatedPlugins);
    }

    private void RestoreToggle(ToggleSwitch toggle, bool enabled)
    {
        _isApplyingPluginState = true;
        try
        {
            toggle.IsOn = enabled;
        }
        finally
        {
            _isApplyingPluginState = false;
        }
    }

    private PluginListItem CreateListItem(PluginInfo plugin)
    {
        if (!BuiltInPluginIds.Contains(plugin.Id))
        {
            // External plugin metadata is supplied by the plugin and remains display data.
            return new PluginListItem(plugin.Id, plugin.Name, plugin.Description, plugin.Enabled);
        }

        var prefix = $"Operations.Plugin.{plugin.Id}";
        return new PluginListItem(
            plugin.Id,
            _strings.Translate($"{prefix}.Name"),
            _strings.Translate($"{prefix}.Description"),
            plugin.Enabled);
    }

    private sealed record PluginListItem(string Id, string Name, string Description, bool Enabled);
}
