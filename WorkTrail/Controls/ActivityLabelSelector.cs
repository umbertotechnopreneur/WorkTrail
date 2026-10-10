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
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Offers an optional active label and persists selection through the shared facade.</summary>
public sealed class ActivityLabelSelector : UserControl
{
    private readonly ComboBox _box = new() { MinWidth = 0, Width = 155, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock _error = new() { FontSize = 11, MaxWidth = 155, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private AppSettings? _settings;
    private IWorkTrailApplication? _application;
    private LocalizationService _strings = new("system");
    private bool _updating;
    private bool _selectionSaveInProgress;
    private readonly ComboBoxItem _manageItem = new();

    /// <summary>Occurs when the user requests the label editor without changing the active label.</summary>
    public event EventHandler? ManageRequested;

    /// <summary>Creates the small selector with an explicit no-label choice.</summary>
    public ActivityLabelSelector()
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(_box);
        panel.Children.Add(_error);
        Content = panel;
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        _box.SelectionChanged += SelectionChanged;
    }

    /// <summary>Reports settings after the selected label has been saved.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Displays the active label and current saved catalog.</summary>
    public void ApplySettings(IWorkTrailApplication application, AppSettings settings)
    {
        _application = application;
        if (_settings is not null && _settings.SpanLabel == settings.SpanLabel && _settings.UiLanguage == settings.UiLanguage
            && (_settings.ActivityLabels ?? []).SequenceEqual(settings.ActivityLabels ?? [])) return;
        _settings = settings;
        _strings = new LocalizationService(settings.UiLanguage);
        _updating = true;
        try
        {
            _box.Items.Clear();
            var none = new ComboBoxItem { Content = _strings.Translate("Labels.None"), Tag = "" };
            _box.Items.Add(none);
            _box.SelectedItem = none;
            _manageItem.Content = _strings.Translate("Labels.Manage") + "…";
            AutomationProperties.SetName(_manageItem, _strings.Translate("Labels.Manage"));
            _box.Items.Add(_manageItem);
            foreach (var label in settings.ActivityLabels ?? [])
            {
                var item = new ComboBoxItem { Content = ActivityLabelVisuals.Content(label), Tag = label.Id };
                AutomationProperties.SetName(item, label.Name);
                _box.Items.Add(item);
                if (label.Name == settings.SpanLabel) _box.SelectedItem = item;
            }
            // The taskbar also supports a one-off text label. Display its live value without adding it to the saved catalog.
            if (settings.SpanLabel.Length > 0 && ReferenceEquals(_box.SelectedItem, none))
            {
                var oneOff = new ComboBoxItem { Content = settings.SpanLabel };
                _box.Items.Add(oneOff);
                _box.SelectedItem = oneOff;
            }
            AutomationProperties.SetName(_box, _strings.Translate("Labels.Title"));
            ToolTipService.SetToolTip(_box, settings.SpanLabel.Length > 0 ? settings.SpanLabel : _strings.Translate("Labels.None"));
        }
        finally { _updating = false; }
    }

    // sender identifies the label selector.
    // e contains the selected command or activity label.
    private async void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && ReferenceEquals(_box.SelectedItem, _manageItem))
        {
            // Management is a command, not an activity-label selection or a settings mutation.
            _updating = true;
            try { RestoreAcknowledgedSelection(); }
            finally { _updating = false; }
            _box.IsDropDownOpen = false;
            ManageRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_updating || _application is null || _box.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _box.IsEnabled = false;
        _selectionSaveInProgress = true;
        _error.Visibility = Visibility.Collapsed;
        try
        {
            // Keep the last acknowledged selection if persistence or validation fails.
            var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { ["activity.label.select"] = id }), CancellationToken.None);
            if (!result.Succeeded || result.Value is null)
            {
                RestoreSelection();
                _error.Text = _strings.Translate(result.MessageKey);
                return;
            }
            ApplySettings(_application, result.Value);
            SettingsSaved?.Invoke(result.Value);
        }
        catch (Exception) { RestoreSelection(); }
        finally { _selectionSaveInProgress = false; _box.IsEnabled = true; }
    }

    /// <summary>Reflects the runtime's active label after a profile switch or taskbar edit.</summary>
    internal void ApplyActiveLabel(string name)
    {
        if (!_selectionSaveInProgress && _application is not null && _settings is not null)
            ApplySettings(_application, _settings with { SpanLabel = name });
    }

    private void RestoreSelection()
    {
        _updating = true;
        try
        {
            RestoreAcknowledgedSelection();
        }
        finally { _updating = false; }
        _error.Text = _strings.Translate("Labels.SaveError");
        _error.Visibility = Visibility.Visible;
    }

    private void RestoreAcknowledgedSelection()
    {
        var id = (_settings?.ActivityLabels ?? []).FirstOrDefault(label => label.Name == _settings?.SpanLabel)?.Id;
        _box.SelectedItem = _box.Items.Cast<ComboBoxItem>().First(item =>
            !ReferenceEquals(item, _manageItem)
            && (_settings?.SpanLabel.Length > 0 ? item.Tag as string == id : item.Tag as string == ""));
    }
}
