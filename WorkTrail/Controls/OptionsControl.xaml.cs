// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Displays option controls and forwards typed requests to the shared application facade.</summary>
public sealed partial class OptionsControl : UserControl
{
    private static readonly HashSet<string> BuiltInModelKeys = new(StringComparer.Ordinal)
    {
        "gpt-5.6-sol",
        "gpt-5.6-terra",
        "gpt-5.6-luna",
        "gpt-5.3-codex-spark",
        "gpt-5.5",
        "gpt-5.4",
        "gpt-5.4-mini",
        "gpt-5.4-nano"
    };

    private IWorkTrailApplication? _application;
    private AiApplicationState? _aiState;
    private LocalizationService _strings = new("system");
    private IReadOnlyList<AiModelOption> _modelOptions = Array.Empty<AiModelOption>();
    private string _requestedThinkingEffort = "auto";
    private bool _updatingAiToggle;
    private bool _updatingAiDailyLimit;
    private bool _suppressAutoSave;
    private bool _autoSaveReady;
    private int? _openAiDailyLimit;
    private Task _autoSaveQueue = Task.CompletedTask;
    private AppSettings? _lastPersistedSettings;

    /// <summary>Initializes the options control.</summary>
    public OptionsControl()
    {
        InitializeComponent();
        RegisterAutoSaveHandlers();
    }

    /// <summary>Occurs when the host should restore the player panel.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Occurs after a successfully persisted application settings snapshot is returned.</summary>
    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Occurs after visible option content changes and the host may need to re-measure its window.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Occurs when the user requests a real, bounded test of the saved AI connection.</summary>
    public event EventHandler? AiConnectionTestRequested;

    /// <summary>Occurs when the user requests the dedicated search-index progress window.</summary>
    public event EventHandler? SearchIndexingRequested;

    /// <summary>Occurs when the user requests the dedicated label-management dialog.</summary>
    public event EventHandler? ManageLabelsRequested;

    private void ManageLabels_Click(object sender, RoutedEventArgs e) => ManageLabelsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Occurs when the user requests one focused local-data or operations surface.</summary>
    internal event Action<OperationsSection>? OperationsSectionRequested;

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        var wasSuppressingAutoSave = _suppressAutoSave;
        _suppressAutoSave = true;
        try
        {
            _strings = new LocalizationService(language);
            UiLocalization.Apply(this, _strings);
            ApplyLanguagePickerPresentation();
            if (SelectedModel() is { } selectedModel)
            {
                PopulateThinkingEfforts(selectedModel, SelectedTag(AiReasoningEffortBox, _requestedThinkingEffort));
                ModelDescriptionText.Text = LocalizedModelDescription(selectedModel);
            }
            AutomationProperties.SetName(TaskbarWidgetVisibleSwitch, T("Options.TaskbarWidget.Visible"));
            AutomationProperties.SetName(MainWindowOpacitySlider, T("Options.Window.Opacity.Header"));
            AutomationProperties.SetName(AutoHideTitleBarSwitch, T("Options.Window.AutoHideTitleBar.Header"));
            AutomationProperties.SetHelpText(AutoHideTitleBarSwitch, T("Options.Window.AutoHideTitleBar.Description"));
            AutomationProperties.SetName(WindowSnappingSwitch, T("Options.Window.Snapping.Header"));
            AutomationProperties.SetHelpText(WindowSnappingSwitch, T("Options.Window.Snapping.Description"));
            AutomationProperties.SetName(MainWindowShowInTaskbarSwitch, T("Options.Window.ShowInTaskbar.Header"));
            AutomationProperties.SetName(StartWithWindowsSwitch, T("Options.StartWithWindows.Header"));
            AutomationProperties.SetName(StartTrackingOnLaunchSwitch, T("Options.StartTracking.Header"));
            AutomationProperties.SetName(ScreenshotsEnabledSwitch, T("Options.SnapshotsEnabled.Header"));
            AutomationProperties.SetName(OcrAiSettingsButton, T("Options.AiConfiguration.Title"));
            AutomationProperties.SetName(SearchSynonymsSwitch, T("Options.Search.Synonyms"));
            AutomationProperties.SetName(SearchTypoToleranceSwitch, T("Options.Search.TypoTolerance"));
            AutomationProperties.SetName(OcrEnabledSwitch, T("Options.Ocr.Enabled"));
            AutomationProperties.SetName(RebuildSearchIndexButton, T("Options.Search.Rebuild"));
            AutomationProperties.SetName(ShowAiMonthlySpendSwitch, T("Options.AiMonthlySpendVisibility.Header"));
            AutomationProperties.SetName(AiDailyLimitExpander, T("Options.AiQuota.Configure"));
            AutomationProperties.SetHelpText(AiDailyLimitExpander, T("Options.AiQuota.LimitHint"));
            AiDailyLimitActionText.Text = T("Options.AiQuota.Configure");
            AiDailyLimitBox.Header = T("Options.AiQuota.Limit");
            AiDailyLimitHintText.Text = T("Options.AiQuota.LimitHint");
            AutomationProperties.SetName(AiDailyLimitBox, T("Options.AiQuota.Limit"));
            AutomationProperties.SetHelpText(AiDailyLimitBox, T("Options.AiQuota.LimitHint"));
            AutomationProperties.SetName(SnapshotAiOperationsLink, T("Options.Navigation.SnapshotAi.Action"));
            AutomationProperties.SetHelpText(SnapshotAiOperationsLink, T("Options.Navigation.SnapshotAi.Description"));
            AutomationProperties.SetName(PrivacyOperationsLink, T("Options.Navigation.Privacy.Action"));
            AutomationProperties.SetHelpText(PrivacyOperationsLink, T("Options.Navigation.Privacy.Description"));
            AutomationProperties.SetName(RetentionOperationsLink, T("Options.Navigation.Retention.Action"));
            AutomationProperties.SetHelpText(RetentionOperationsLink, T("Options.Navigation.Retention.Description"));
            AutomationProperties.SetName(PluginsOperationsLink, T("Options.Navigation.Plugins.Action"));
            AutomationProperties.SetHelpText(PluginsOperationsLink, T("Options.Navigation.Plugins.Description"));
            UpdateApiKeyPresentation();
            UpdateAiQuotaPresentation();
            UpdateScreenshotModeHint();
            NotifyLayoutChanged();
        }
        finally
        {
            _suppressAutoSave = wasSuppressingAutoSave;
        }
    }

    /// <summary>Shows every UI-language choice with a flag, its English name, and its native name.</summary>
    private void ApplyLanguagePickerPresentation()
    {
        var displays = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["system"] = $"🌐 System{SystemLanguageSuffix()}",
            ["zh-Hans"] = "🇨🇳 Chinese (Simplified) · 简体中文",
            ["en-US"] = "🇺🇸 English (United States)",
            ["fr-FR"] = "🇫🇷 French · Français",
            ["de-DE"] = "🇩🇪 German · Deutsch",
            ["it-IT"] = "🇮🇹 Italian · Italiano",
            ["ko-KR"] = "🇰🇷 Korean · 한국어",
            ["pt-BR"] = "🇧🇷 Portuguese (Brazil) · Português (Brasil)",
            ["pt-PT"] = "🇵🇹 Portuguese (Portugal) · Português (Portugal)",
            ["es-ES"] = "🇪🇸 Spanish · Español",
            ["vi-VN"] = "🇻🇳 Vietnamese · Tiếng Việt"
        };

        foreach (var item in LanguageBox.Items.OfType<ComboBoxItem>())
        {
            var language = item.Tag?.ToString() ?? throw new InvalidOperationException("Language selection item is missing its language tag.");
            if (!displays.TryGetValue(language, out var display))
            {
                throw new InvalidOperationException($"Unsupported UI language '{language}' in the language picker.");
            }

            item.Content = display;
            AutomationProperties.SetName(item, display);
        }

        AutomationProperties.SetName(LanguageBox, T("Options.Language.Header"));
    }

    private string SystemLanguageSuffix()
    {
        var localized = T("Options.Language.system");
        return string.Equals(localized, "System", StringComparison.OrdinalIgnoreCase) ? string.Empty : $" · {localized}";
    }

    /// <summary>Attaches the shared application facade and loads persisted settings into controls.</summary>
    public async Task InitializeAsync(IWorkTrailApplication application, AiApplicationState aiState, CancellationToken cancellationToken)
    {
        _application = application;
        if (_aiState is not null)
        {
            _aiState.PropertyChanged -= AiState_PropertyChanged;
        }
        _aiState = aiState;
        _aiState.PropertyChanged += AiState_PropertyChanged;
        DataContext = aiState;
        UpdateApiKeyPresentation();
        var settingsTask = application.GetSettingsAsync(cancellationToken);
        var catalogTask = application.GetAiModelCatalogAsync(cancellationToken);
        var aiStateTask = aiState.LoadAsync(cancellationToken);
        await Task.WhenAll(settingsTask, catalogTask, aiStateTask);

        var settingsResult = await settingsTask;
        var catalogResult = await catalogTask;
        if (!catalogResult.Succeeded || catalogResult.Value is null || catalogResult.Value.Models.Count == 0)
        {
            StatusText.Text = T("Options.ModelCatalogError");
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        ConfigureModelOptions(catalogResult.Value);
        if (settingsResult.Succeeded && settingsResult.Value is not null)
        {
            ApplySettings(settingsResult.Value);
        }
    }

    /// <summary>Moves from OCR and AI configuration to general options, then back to the player.</summary>
    public void NavigateBack()
    {
        if (OcrAiOptionsView.Visibility == Visibility.Visible)
        {
            OcrAiOptionsView.Visibility = Visibility.Collapsed;
            AppOptionsView.Visibility = Visibility.Visible;
            NotifyLayoutChanged();
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the paired OCR and AI configuration view without changing persisted settings.</summary>
    private async void OcrAiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        AppOptionsView.Visibility = Visibility.Collapsed;
        OcrAiOptionsView.Visibility = Visibility.Visible;
        NotifyLayoutChanged();
        if (_aiState is not null)
        {
            await _aiState.LoadAsync(CancellationToken.None);
        }
        UpdateAiQuotaPresentation();
    }

    private void SnapshotAiOperationsLink_Click(object sender, RoutedEventArgs e) =>
        OperationsSectionRequested?.Invoke(OperationsSection.SnapshotAi);

    private void PrivacyOperationsLink_Click(object sender, RoutedEventArgs e) =>
        OperationsSectionRequested?.Invoke(OperationsSection.Privacy);

    private void RetentionOperationsLink_Click(object sender, RoutedEventArgs e) =>
        OperationsSectionRequested?.Invoke(OperationsSection.Retention);

    private void PluginsOperationsLink_Click(object sender, RoutedEventArgs e) =>
        OperationsSectionRequested?.Invoke(OperationsSection.Plugins);

    private void OcrEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        OcrLanguageBox.IsEnabled = OcrEnabledSwitch.IsOn;
        QueueAutoSave("ocr.enabled", OcrEnabledSwitch.IsOn ? "true" : "false");
    }

    /// <summary>Requests the detached progress surface; the control does not own top-level windows.</summary>
    private void RebuildSearchIndexButton_Click(object sender, RoutedEventArgs e) =>
        SearchIndexingRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Maintains a single selected theme in the segmented theme control.</summary>
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeSystemButton.IsChecked = ReferenceEquals(sender, ThemeSystemButton);
        ThemeLightButton.IsChecked = ReferenceEquals(sender, ThemeLightButton);
        ThemeDarkButton.IsChecked = ReferenceEquals(sender, ThemeDarkButton);
        QueueAutoSave("theme", SelectedTheme());
    }

    private void TaskbarWidgetVisibleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        TaskbarWidgetPositionBox.IsEnabled = TaskbarWidgetVisibleSwitch.IsOn;
        QueueAutoSave("taskbar.widget.visible", TaskbarWidgetVisibleSwitch.IsOn ? "true" : "false");
    }

    /// <summary>Shows or hides lower-frequency application settings.</summary>
    private void GeneralAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        var show = GeneralAdvancedPanel.Visibility != Visibility.Visible;
        GeneralAdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GeneralAdvancedChevron.Glyph = show ? "\uE70E" : "\uE70D";
        NotifyLayoutChanged();
    }

    /// <summary>Shows or hides lower-frequency provider and analysis settings.</summary>
    private void AiAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        var show = AiAdvancedPanel.Visibility != Visibility.Visible;
        AiAdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AiAdvancedChevron.Glyph = show ? "\uE70E" : "\uE70D";
        NotifyLayoutChanged();
    }

    /// <summary>Keeps the host window measurement synchronized with the API-key expander.</summary>
    private void ApiKeyExpander_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5)
        {
            NotifyLayoutChanged();
        }
    }

    /// <summary>Keeps the host window measurement synchronized with the daily-limit expander.</summary>
    private void AiDailyLimitExpander_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5)
        {
            NotifyLayoutChanged();
        }
    }

    /// <summary>Forwards a secret to the application facade without placing it in settings or UI state.</summary>
    private async void SetApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_application is null || _aiState is null || string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            ShowStatus(T("ApiKeyMissing"));
            return;
        }

        var provider = SelectedTag(AiProviderBox, "openai");
        var keyName = string.IsNullOrWhiteSpace(AiApiKeyNameBox.Text) ? DefaultApiKeyName(provider) : AiApiKeyNameBox.Text.Trim();
        var secret = ApiKeyBox.Password;
        ApiKeyBox.Password = string.Empty;
        var button = (Button)sender;
        button.IsEnabled = false;
        ShowStatus(T("ProviderSetup.Verifying"));
        try
        {
            var result = await _aiState.SetSecretAsync(keyName, secret, CancellationToken.None);
            var keyReady = result.Succeeded && _aiState.CanEnable;
            if (keyReady)
            {
                ApiKeyExpander.IsExpanded = false;
            }

            ShowStatus(keyReady
                ? T("ProviderSetup.Success")
                : result.Succeeded
                    ? T("Options.ApiKeyStatus.Invalid")
                    : result.Code == "ai.key.stored_status_unavailable"
                        ? T("Options.ApiKeyStatus.RefreshFailed")
                        : T(result.MessageKey));
        }
        catch (Exception)
        {
            ShowStatus(T("ProviderSetup.Error.Unavailable"));
        }
        finally
        {
            secret = string.Empty;
            button.IsEnabled = true;
        }
    }

    /// <summary>Forwards an AI enabled-state request through the shared observable application state.</summary>
    private async void OpenAiEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_aiState is null || _updatingAiToggle || OpenAiEnabledSwitch.IsOn == _aiState.Enabled)
        {
            return;
        }

        var result = await _aiState.SetEnabledAsync(OpenAiEnabledSwitch.IsOn, CancellationToken.None);
        if (result.Succeeded)
        {
            return;
        }

        _updatingAiToggle = true;
        OpenAiEnabledSwitch.IsOn = _aiState.Enabled;
        _updatingAiToggle = false;
        ShowStatus(T("Options.OpenAi.KeyRequired"));
    }

    /// <summary>Queues the validated daily screenshot-processing limit for immediate persistence.</summary>
    private void QueueAiDailyLimitSave()
    {
        if (_updatingAiDailyLimit || !CanAutoSave)
        {
            return;
        }

        var requestedValue = AiDailyLimitBox.Value;
        if (!double.IsFinite(requestedValue)
            || requestedValue != Math.Truncate(requestedValue)
            || requestedValue < SettingsCatalog.MinimumAiDailyLimit
            || requestedValue > SettingsCatalog.MaximumAiDailyLimit)
        {
            ShowStatus(T("Options.AiQuota.Invalid"));
            return;
        }

        QueueAutoSave(
            "ai.daily_limit",
            ((int)requestedValue).ToString(CultureInfo.InvariantCulture),
            refreshAiState: true);
    }

    /// <summary>Queues a single typed setting change and serializes it with preceding local edits.</summary>
    private void QueueAutoSave(string key, string? value, bool refreshAiState = false) =>
        QueueAutoSave(new Dictionary<string, string?> { [key] = value }, refreshAiState);

    /// <summary>Queues related typed setting changes as one atomic application request.</summary>
    private void QueueAutoSave(IReadOnlyDictionary<string, string?> values, bool refreshAiState = false)
    {
        if (!CanAutoSave)
        {
            return;
        }

        var patch = new SettingsPatch(new Dictionary<string, string?>(values));
        _autoSaveQueue = PersistQueuedPatchAsync(_autoSaveQueue, patch, refreshAiState);
    }

    /// <summary>Persists a queued option patch after earlier edits so rapid UI changes retain their final order.</summary>
    private async Task PersistQueuedPatchAsync(Task precedingSave, SettingsPatch patch, bool refreshAiState)
    {
        try
        {
            await precedingSave;
        }
        catch (Exception)
        {
            // The previous failure is already shown and must not block the user's later correction.
        }

        if (_application is null)
        {
            return;
        }

        OperationResult<AppSettings> result;
        try
        {
            result = await _application.PatchSettingsAsync(patch, CancellationToken.None);
        }
        catch (Exception)
        {
            // A persistence or IPC exception leaves the last validated snapshot authoritative.
            await RestorePersistedSettingsAfterFailureAsync();
            ShowStatus(T("Options.SaveError"));
            return;
        }

        if (result.Succeeded && result.Value is not null)
        {
            ApplySettings(result.Value);
            if (_aiState is not null)
            {
                if (refreshAiState)
                {
                    await _aiState.LoadAsync(CancellationToken.None);
                }
            }
            SettingsSaved?.Invoke(result.Value);
            ShowStatus(T("OptionsSaved"));
        }
        else
        {
            await RestorePersistedSettingsAfterFailureAsync();
            ShowStatus(T("Options.SaveError"));
        }
    }

    /// <summary>Forwards the explicit connection-test intent to the owning Mica dialog coordinator.</summary>
    private void TestConnectionButton_Click(object sender, RoutedEventArgs e) => AiConnectionTestRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Applies provider defaults as presentation convenience only.</summary>
    private void AiProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = SelectedTag(AiProviderBox, "openai");
        if (IsDefaultOrKnownEndpoint(AiEndpointBox.Text)) AiEndpointBox.Text = DefaultEndpoint(provider);
        if (IsDefaultOrKnownApiKeyName(AiApiKeyNameBox.Text)) AiApiKeyNameBox.Text = DefaultApiKeyName(provider);
        QueueAutoSave(
            new Dictionary<string, string?>
            {
                ["ai.provider"] = provider,
                ["ai.endpoint"] = AiEndpointBox.Text.Trim(),
                ["ai.key_variable"] = AiApiKeyNameBox.Text.Trim()
            },
            refreshAiState: true);
    }

    /// <summary>Updates the local capture-mode hint.</summary>
    private void ScreenshotModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateScreenshotModeHint();
        QueueAutoSave("screenshots.mode", SelectedTag(ScreenshotModeBox, "all-screens"));
    }

    private void ApplySettings(AppSettings settings)
    {
        _lastPersistedSettings = settings;
        var wasSuppressingAutoSave = _suppressAutoSave;
        _suppressAutoSave = true;
        try
        {
            _openAiDailyLimit = settings.OpenAiDailyLimit;
            _updatingAiDailyLimit = true;
            AiDailyLimitBox.Value = settings.OpenAiDailyLimit;
            _updatingAiDailyLimit = false;
            ApplyLanguage(settings.UiLanguage);
            _requestedThinkingEffort = settings.AiReasoningEffort;
            SelectModel(settings.Model);
            ScreenshotsEnabledSwitch.IsOn = settings.ScreenshotsEnabled;
            SelectTag(SearchLanguageBox, settings.SearchLanguage, "system");
            SearchSynonymsSwitch.IsOn = settings.SearchSynonymsEnabled;
            SearchTypoToleranceSwitch.IsOn = settings.SearchTypoToleranceEnabled;
            OcrEnabledSwitch.IsOn = settings.OcrEnabled;
            SelectTag(OcrLanguageBox, settings.OcrLanguage, "system");
            OcrLanguageBox.IsEnabled = settings.OcrEnabled;
            StartWithWindowsSwitch.IsOn = settings.StartWithWindows;
            StartTrackingOnLaunchSwitch.IsOn = settings.StartTrackingOnLaunch;
            SelectTag(AiProviderBox, settings.AiProvider, "openai");
            AiEndpointBox.Text = string.IsNullOrWhiteSpace(settings.AiEndpoint) ? DefaultEndpoint(settings.AiProvider) : settings.AiEndpoint;
            AiApiKeyNameBox.Text = string.IsNullOrWhiteSpace(settings.AiApiKeyName) ? DefaultApiKeyName(settings.AiProvider) : settings.AiApiKeyName;
            SelectTag(AiOutputDetailBox, settings.AiOutputDetail, "balanced");
            SelectTag(AiReasoningEffortBox, settings.AiReasoningEffort, "auto");
            SelectTag(ScreenshotModeBox, settings.ScreenshotCaptureMode, "all-screens");
            SelectTag(LanguageBox, settings.UiLanguage, "system");
            SelectTag(PositionBox, settings.FlyoutPosition, "bottom-center");
            MainWindowOpacitySlider.Value = settings.MainWindowOpacityPercent;
            AutoHideTitleBarSwitch.IsOn = settings.AutoHideTitleBar;
            WindowSnappingSwitch.IsOn = settings.WindowSnappingEnabled;
            MainWindowShowInTaskbarSwitch.IsOn = settings.MainWindowShowInTaskbar;
            TaskbarWidgetVisibleSwitch.IsOn = settings.TaskbarWidgetVisible;
            SelectTag(TaskbarWidgetPositionBox, settings.TaskbarWidgetPosition, "left");
            TaskbarWidgetPositionBox.IsEnabled = settings.TaskbarWidgetVisible;
            SelectTheme(settings.Theme);
            AiCustomPromptBox.Text = settings.AiCustomPrompt;
            IncludeDeviceLocationSwitch.IsOn = settings.IncludeDeviceLocation;
            ShowAiMonthlySpendSwitch.IsOn = settings.ShowAiMonthlySpend;
            UpdateAiQuotaPresentation();
            UpdateScreenshotModeHint();
            NotifyLayoutChanged();
        }
        finally
        {
            _suppressAutoSave = wasSuppressingAutoSave;
        }

        _autoSaveReady = true;
    }

    /// <summary>Refreshes the passive options controls from a settings snapshot persisted by another surface.</summary>
    internal void ApplyExternalSettings(AppSettings settings) => ApplySettings(settings);

    private bool CanAutoSave => _autoSaveReady && !_suppressAutoSave && _application is not null;

    /// <summary>Connects ordinary option controls to their immediate, typed persistence route.</summary>
    private void RegisterAutoSaveHandlers()
    {
        PositionBox.SelectionChanged += (_, _) => QueueAutoSave("position", SelectedTag(PositionBox, "bottom-center"));
        AutoHideTitleBarSwitch.Toggled += (_, _) => QueueAutoSave(
            "window.titlebar.auto_hide",
            AutoHideTitleBarSwitch.IsOn ? "true" : "false");
        WindowSnappingSwitch.Toggled += (_, _) => QueueAutoSave(
            "window.snapping.enabled",
            WindowSnappingSwitch.IsOn ? "true" : "false");
        MainWindowOpacitySlider.ValueChanged += (_, _) => QueueAutoSave(
            "window.main.opacity_percent",
            MainWindowOpacitySlider.Value.ToString("0", CultureInfo.InvariantCulture));
        MainWindowShowInTaskbarSwitch.Toggled += (_, _) => QueueAutoSave(
            "window.main.show_in_taskbar",
            MainWindowShowInTaskbarSwitch.IsOn ? "true" : "false");
        TaskbarWidgetPositionBox.SelectionChanged += (_, _) => QueueAutoSave(
            "taskbar.widget.position",
            SelectedTag(TaskbarWidgetPositionBox, "left"));
        LanguageBox.SelectionChanged += (_, _) => QueueAutoSave("language", SelectedTag(LanguageBox, "system"));
        StartWithWindowsSwitch.Toggled += (_, _) => QueueAutoSave(
            "startup.enabled",
            StartWithWindowsSwitch.IsOn ? "true" : "false");
        StartTrackingOnLaunchSwitch.Toggled += (_, _) => QueueAutoSave(
            "tracking.start_on_launch",
            StartTrackingOnLaunchSwitch.IsOn ? "true" : "false");
        ScreenshotsEnabledSwitch.Toggled += (_, _) => QueueAutoSave(
            "screenshots.enabled",
            ScreenshotsEnabledSwitch.IsOn ? "true" : "false");
        SearchLanguageBox.SelectionChanged += (_, _) => QueueAutoSave("search.language", SelectedTag(SearchLanguageBox, "system"));
        SearchSynonymsSwitch.Toggled += (_, _) => QueueAutoSave(
            "search.synonyms",
            SearchSynonymsSwitch.IsOn ? "true" : "false");
        SearchTypoToleranceSwitch.Toggled += (_, _) => QueueAutoSave(
            "search.typo_tolerance",
            SearchTypoToleranceSwitch.IsOn ? "true" : "false");
        OcrLanguageBox.SelectionChanged += (_, _) => QueueAutoSave("ocr.language", SelectedTag(OcrLanguageBox, "system"));
        AiDailyLimitBox.ValueChanged += (_, _) => QueueAiDailyLimitSave();
        AiReasoningEffortBox.SelectionChanged += (_, _) => QueueReasoningEffortSave();
        AiOutputDetailBox.SelectionChanged += (_, _) => QueueAutoSave(
            "ai.output_detail",
            SelectedTag(AiOutputDetailBox, "balanced"),
            refreshAiState: true);
        AiCustomPromptBox.LostFocus += (_, _) => QueueAutoSave("ai.custom_prompt", AiCustomPromptBox.Text, refreshAiState: true);
        IncludeDeviceLocationSwitch.Toggled += (_, _) => QueueAutoSave(
            "ai.include_device_location",
            IncludeDeviceLocationSwitch.IsOn ? "true" : "false",
            refreshAiState: true);
        ShowAiMonthlySpendSwitch.Toggled += (_, _) => QueueAutoSave(
            "ai.show_monthly_spend",
            ShowAiMonthlySpendSwitch.IsOn ? "true" : "false");
        AiEndpointBox.LostFocus += (_, _) => QueueAutoSave("ai.endpoint", AiEndpointBox.Text.Trim(), refreshAiState: true);
        AiApiKeyNameBox.LostFocus += (_, _) => QueueAutoSave("ai.key_variable", AiApiKeyNameBox.Text.Trim(), refreshAiState: true);
    }

    /// <summary>Persists a valid reasoning effort only with the currently selected, image-capable model.</summary>
    private void QueueReasoningEffortSave()
    {
        var model = SelectedModel();
        if (!CanAutoSave || model is null || !model.SupportsImageInput)
        {
            return;
        }

        var thinkingEffort = SelectedTag(AiReasoningEffortBox, "auto");
        if (!model.SupportedThinkingEfforts.Contains(thinkingEffort, StringComparer.Ordinal))
        {
            ShowStatus(T("Options.ThinkingEffortUnsupported"));
            return;
        }

        _requestedThinkingEffort = thinkingEffort;
        QueueAutoSave("ai.reasoning_effort", thinkingEffort, refreshAiState: true);
    }

    /// <summary>Restores the last persisted settings when a typed change is rejected by the application layer.</summary>
    private async Task RestorePersistedSettingsAfterFailureAsync()
    {
        if (_lastPersistedSettings is { } authoritative)
        {
            ApplySettings(authoritative);
        }
        if (_application is null)
        {
            return;
        }

        try
        {
            var current = await _application.GetSettingsAsync(CancellationToken.None);
            if (current.Succeeded && current.Value is not null)
            {
                ApplySettings(current.Value);
            }
        }
        catch (Exception)
        {
            // Keep the last confirmed settings when even the refresh fails; the caller displays the save error.
        }
    }

    /// <summary>Renders a concise, accessible persistence status only when there is feedback to show.</summary>
    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        NotifyLayoutChanged();
    }

    private void UpdateScreenshotModeHint() => ScreenshotModeHintBox.Text = SelectedTag(ScreenshotModeBox, "all-screens") == "active-window" ? T("Options.SnapshotHintActive") : T("Options.SnapshotHintAll");

    private void ConfigureModelOptions(AiModelCatalogSnapshot catalog)
    {
        _modelOptions = catalog.Models
            .Select(model => new AiModelOption(model, CreateBrush(model.Color)))
            .ToArray();
        ModelBox.ItemsSource = _modelOptions;
    }

    private void SelectModel(string identifier)
    {
        ModelBox.SelectedItem = _modelOptions.FirstOrDefault(option =>
            string.Equals(option.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
            option.Aliases.Contains(identifier, StringComparer.OrdinalIgnoreCase));
        if (ModelBox.SelectedItem is null)
        {
            ModelInfoCard.Visibility = Visibility.Collapsed;
            StatusText.Text = T("Options.ModelUnsupported");
            NotifyLayoutChanged();
        }
    }

    private AiModelDescriptor? SelectedModel() => (ModelBox.SelectedItem as AiModelOption)?.Descriptor;

    private void ModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var model = SelectedModel();
        if (model is null)
        {
            ModelInfoCard.Visibility = Visibility.Collapsed;
            NotifyLayoutChanged();
            return;
        }

        var preferredEffort = AiReasoningEffortBox.SelectedItem is null
            ? _requestedThinkingEffort
            : SelectedTag(AiReasoningEffortBox, _requestedThinkingEffort);
        var wasSuppressingAutoSave = _suppressAutoSave;
        _suppressAutoSave = true;
        PopulateThinkingEfforts(model, preferredEffort);
        _suppressAutoSave = wasSuppressingAutoSave;
        _requestedThinkingEffort = SelectedTag(AiReasoningEffortBox, "auto");
        var option = (AiModelOption)ModelBox.SelectedItem;
        ModelAccentBar.Background = option.AccentBrush;
        ModelInfoCard.BorderBrush = option.AccentBrush;
        ModelDescriptionText.Text = LocalizedModelDescription(model);
        ModelKeyText.Text = model.Key;
        ModelPreviewBadge.Visibility = model.IsPreview ? Visibility.Visible : Visibility.Collapsed;
        ModelCapabilityText.Text = T(model.SupportsImageInput ? "Options.ModelImageInput" : "Options.ModelTextOnly");
        ModelCapabilityBadge.Visibility = Visibility.Visible;
        ModelInfoCard.Visibility = Visibility.Visible;
        StatusText.Text = model.SupportsImageInput ? string.Empty : T("Options.ModelImageUnsupported");
        NotifyLayoutChanged();

        if (!model.SupportsImageInput)
        {
            ShowStatus(T("Options.ModelImageUnsupported"));
            return;
        }

        QueueAutoSave(
            new Dictionary<string, string?>
            {
                ["ai.model"] = model.Key,
                ["ai.reasoning_effort"] = _requestedThinkingEffort
            },
            refreshAiState: true);
    }

    private void NotifyLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    private string LocalizedModelDescription(AiModelDescriptor model)
    {
        if (BuiltInModelKeys.Contains(model.Key))
        {
            return T($"Options.Model.Description.{model.Key}");
        }

        // Explicitly loaded external model catalogs own their descriptive metadata.
        return model.Description;
    }

    private void PopulateThinkingEfforts(AiModelDescriptor model, string preferredEffort)
    {
        AiReasoningEffortBox.Items.Clear();
        foreach (var effort in model.SupportedThinkingEfforts)
        {
            AiReasoningEffortBox.Items.Add(new ComboBoxItem
            {
                Tag = effort,
                Content = T($"Options.Reasoning.{effort}")
            });
        }

        SelectTag(AiReasoningEffortBox, preferredEffort, "auto");
        AiReasoningEffortBox.IsEnabled = AiReasoningEffortBox.Items.Count > 1;
    }

    private static SolidColorBrush CreateBrush(string hexColor)
    {
        var red = Convert.ToByte(hexColor.Substring(1, 2), 16);
        var green = Convert.ToByte(hexColor.Substring(3, 2), 16);
        var blue = Convert.ToByte(hexColor.Substring(5, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, red, green, blue));
    }

    private void AiState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiApplicationState.HasKey)
            or nameof(AiApplicationState.CanEnable)
            or nameof(AiApplicationState.Enabled)
            or nameof(AiApplicationState.IsStatusUnavailable))
        {
            UpdateApiKeyPresentation();
        }

        if (e.PropertyName == nameof(AiApplicationState.CostGate))
        {
            UpdateAiQuotaPresentation();
        }
    }

    private void UpdateApiKeyPresentation()
    {
        AutomationProperties.SetName(OpenAiEnabledSwitch, T("Options.OpenAi.Header"));
        AutomationProperties.SetHelpText(
            OpenAiEnabledSwitch,
            _aiState is null || _aiState.IsStatusUnavailable
                ? T("Options.ApiKeyStatus.Unavailable")
                : !_aiState.CanEnable && !_aiState.Enabled
                    ? T("Options.OpenAi.KeyRequired")
                    : string.Empty);

        var (messageKey, glyph, visualState, actionKey) = _aiState switch
        {
            null => ("Options.ApiKeyStatus.Unavailable", "\uE7BA", "ApiKeyNeedsAttention", "Options.ApiKeyAction.Set"),
            { IsStatusUnavailable: true } => ("Options.ApiKeyStatus.Unavailable", "\uE7BA", "ApiKeyNeedsAttention", "Options.ApiKeyAction.Set"),
            { CanEnable: true } => ("Options.ApiKeyStatus.Set", "\uE73E", "ApiKeyReady", "Options.ApiKeyAction.Change"),
            { HasKey: false } => ("Options.ApiKeyStatus.Missing", "\uE7BA", "ApiKeyNeedsAttention", "Options.ApiKeyAction.Set"),
            _ => ("Options.ApiKeyStatus.Invalid", "\uE7BA", "ApiKeyNeedsAttention", "Options.ApiKeyAction.Set")
        };

        var actionLabel = T(actionKey);
        ApiKeyStatusIcon.Glyph = glyph;
        ApiKeyStatusText.Text = T(messageKey);
        ApiKeyActionText.Text = actionLabel;
        AutomationProperties.SetName(ApiKeyExpander, actionLabel);
        AutomationProperties.SetHelpText(ApiKeyExpander, ApiKeyStatusText.Text);
        VisualStateManager.GoToState(this, visualState, false);
        NotifyLayoutChanged();
    }

    private void UpdateAiQuotaPresentation()
    {
        var title = T("Options.AiQuota.Title");
        var description = T("Options.AiQuota.Description");
        AiQuotaTitleText.Text = title;
        AiQuotaDescriptionText.Text = description;
        AutomationProperties.SetName(AiQuotaPanel, title);
        AutomationProperties.SetHelpText(AiQuotaPanel, description);

        var costGate = _aiState?.CostGate;
        if (costGate is null || !_openAiDailyLimit.HasValue)
        {
            var unavailable = T("Options.AiQuota.Unavailable");
            AiQuotaStateText.Text = unavailable;
            AiQuotaUsageText.Text = "– / –";
            AiQuotaProgressBar.Maximum = 1;
            AiQuotaProgressBar.Value = 0;
            AutomationProperties.SetName(AiQuotaProgressBar, T("Options.AiQuota.UnavailableAccessible"));
            AutomationProperties.SetHelpText(AiQuotaProgressBar, description);
            VisualStateManager.GoToState(this, "AiQuotaUnavailable", false);
            NotifyLayoutChanged();
            return;
        }

        var used = Math.Max(0, costGate.DailyAnalysisCount);
        var limit = Math.Max(0, _openAiDailyLimit.Value);
        var reached = limit == 0
            || used >= limit
            || string.Equals(costGate.Reason, "daily_limit", StringComparison.Ordinal);
        var status = T(reached ? "Options.AiQuota.Reached" : "Options.AiQuota.Available");
        var progressMaximum = Math.Max(1, limit);

        AiQuotaStateText.Text = status;
        AiQuotaUsageText.Text = _strings.Format("Options.AiQuota.Usage", used, limit);
        AiQuotaProgressBar.Maximum = progressMaximum;
        AiQuotaProgressBar.Value = limit == 0 ? progressMaximum : Math.Clamp(used, 0, progressMaximum);
        AutomationProperties.SetName(
            AiQuotaProgressBar,
            _strings.Format("Options.AiQuota.ProgressAccessible", used, limit, status));
        AutomationProperties.SetHelpText(AiQuotaProgressBar, description);
        VisualStateManager.GoToState(this, reached ? "AiQuotaReached" : "AiQuotaAvailable", false);
        NotifyLayoutChanged();
    }

    private string T(string key) => _strings.Translate(key);

    private string SelectedTheme() => ThemeLightButton.IsChecked == true ? "light" : ThemeDarkButton.IsChecked == true ? "dark" : "system";

    private void SelectTheme(string theme)
    {
        ThemeSystemButton.IsChecked = theme is not "light" and not "dark";
        ThemeLightButton.IsChecked = theme == "light";
        ThemeDarkButton.IsChecked = theme == "dark";
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SelectTag(ComboBox comboBox, string value, string fallback) => comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, value)) ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, fallback));

    private static string DefaultEndpoint(string provider) => SettingsCatalog.GetDefaultEndpoint(provider);

    private static string DefaultApiKeyName(string provider) => SettingsCatalog.GetDefaultApiKeyVariable(provider);

    private static bool IsDefaultOrKnownEndpoint(string? endpoint) => string.IsNullOrWhiteSpace(endpoint) || endpoint is "https://api.openai.com/v1/responses" or "https://openrouter.ai/api/v1/chat/completions" or "https://api.anthropic.com/v1/messages";

    private static bool IsDefaultOrKnownApiKeyName(string? keyName) => string.IsNullOrWhiteSpace(keyName) || keyName.Trim() is "WORKTRAIL_OPENAI_APIKEY" or "OPENAI_API_KEY" or "OPENROUTER_API_KEY" or "ANTHROPIC_API_KEY";
}

internal sealed class AiModelOption(AiModelDescriptor descriptor, SolidColorBrush accentBrush)
{
    public AiModelDescriptor Descriptor { get; } = descriptor;

    public string Key => Descriptor.Key;

    public string Name => Descriptor.Name;

    public IReadOnlyList<string> Aliases => Descriptor.Aliases;

    public string CapabilityLabel => Descriptor.SupportsImageInput ? string.Empty : "Text only";

    public SolidColorBrush AccentBrush { get; } = accentBrush;
}
