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


using Spectre.Console;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Cli;

/// <summary>Routes one-shot commands and the REPL through the same application facade calls.</summary>
/// <remarks>Initializes a router with presentation-only dependencies.</remarks>
public sealed partial class CliRouter(IWorkTrailApplication application, CliOutput output, CliOptions options)
{
    private readonly IWorkTrailApplication _application = application;
    private readonly CliOutput _output = output;
    private readonly CliOptions _options = options;

    /// <summary>Runs a command token sequence, or opens the persistent shell when none was supplied.</summary>
    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            if (arguments.Count == 0)
            {
                var denied = await CheckCliAccessAsync(cancellationToken);
                return denied ?? await RunShellAsync(cancellationToken);
            }

            if (!CliCommandCatalog.TryExpandShortcut(arguments, out var expanded))
            {
                var denied = await CheckCliAccessAsync(cancellationToken);
                return denied ?? InvalidCommand();
            }

            return await DispatchAsync(CliCommandCatalog.Normalize(expanded), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _output.WriteResult(OperationResult<object>.Failure("operation.cancelled", "OperationCancelled"));
            return 130;
        }
    }

    private async Task<int> DispatchAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var denied = await CheckCliAccessAsync(cancellationToken);
        if (denied is not null) return denied.Value;

        if (CliCommandCatalog.TryGetHelpTopic(arguments, out var helpTopic))
        {
            return WriteHelp(helpTopic);
        }

        var root = arguments[0].ToLowerInvariant();
        return root switch
        {
            "status" => await StatusAsync(arguments, cancellationToken),
            "runtime" => await RuntimeAsync(arguments, cancellationToken),
            "tracking" => await TrackingAsync(arguments, cancellationToken),
            "session" => await SessionAsync(arguments, cancellationToken),
            "system" => await SystemAsync(arguments, cancellationToken),
            "screenshot" => await ScreenshotAsync(arguments, cancellationToken),
            "screenshots" => await ScreenshotAsync(arguments, cancellationToken),
            "search" => await SearchAsync(arguments, cancellationToken),
            "report" => await ReportAsync(arguments, cancellationToken),
            "world-clock" => await WorldClockAsync(arguments, cancellationToken),
            "hardware" => arguments.Count == 2 && arguments[1].Equals("snapshot", StringComparison.OrdinalIgnoreCase)
                ? await WriteAsync(_application.CaptureHardwareSnapshotAsync(cancellationToken)) : InvalidArguments(),
            "data" => await DataAsync(arguments, cancellationToken),
            "logs" => await LogsAsync(arguments, cancellationToken),
            "access" => arguments.Count == 2 && arguments[1].Equals("status", StringComparison.OrdinalIgnoreCase)
                ? await WriteAsync(_application.GetFeatureAccessAsync(cancellationToken)) : InvalidArguments(),
            "reset" => await ResetAsync(arguments, cancellationToken),
            "ai" => await AiAsync(arguments, cancellationToken),
            "privacy" => await PrivacyAsync(arguments, cancellationToken),
            "retention" => await RetentionAsync(arguments, cancellationToken),
            "plugins" => await PluginsAsync(arguments, cancellationToken),
            "config" => await ConfigAsync(arguments, cancellationToken),
            "settings" => await ConfigAsync(arguments, cancellationToken),
            "startup" => await StartupAsync(arguments, cancellationToken),
            "open" => await OpenAsync(arguments, cancellationToken),
            "about" => arguments.Count == 1 ? await WriteAsync(_application.GetProductInformationAsync(cancellationToken)) : InvalidArguments(),
            "doctor" => arguments.Count == 1 ? await DoctorAsync(cancellationToken) : InvalidArguments(),
            "diagnostics" => arguments.Count == 1 ? await DoctorAsync(cancellationToken) : InvalidCommand(),
            "version" or "--version" => arguments.Count == 1 ? WriteResult(CliBootstrap.CreateVersionResult()) : InvalidArguments(),
            _ => InvalidCommand()
        };
    }

    private async Task<int> RuntimeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
    {
        "health" when arguments.Count == 2 => await WriteAsync(_application.GetRuntimeHealthAsync(cancellationToken)),
        _ => InvalidCommand()
    };

    private async Task<int> StatusAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!TryParseWatchOptions(arguments, 1, out var watch, out var interval))
        {
            return InvalidArguments();
        }
        if (!watch)
        {
            var result = await _application.GetDashboardAsync(cancellationToken);
            _output.WriteResult(result, result.Value is null ? null : _output.RenderDashboard(result.Value));
            return result.Succeeded ? 0 : ExitCodeMapper.Map(result.Code);
        }

        return await WatchAsync(
            _application.GetDashboardAsync,
            _output.RenderDashboard,
            interval,
            cancellationToken);
    }

    private async Task<int> TrackingAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "start" when TryParseOptions(arguments, 2, ["--safe-mode"], [], out var options):
                return await WriteAsync(_application.StartTrackingAsync(new StartTrackingRequest(options.Contains("--safe-mode"), "cli"), cancellationToken));
            case "pause" when arguments.Count == 2:
                return await WriteAsync(_application.PauseTrackingAsync(cancellationToken));
            case "toggle" when arguments.Count == 2:
                return await WriteAsync(_application.ToggleTrackingAsync(cancellationToken));
            default:
                return InvalidCommand();
        }
    }

    private async Task<int> SessionAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
    {
        "last" when arguments.Count == 2 => await WriteAsync(_application.GetLastSessionAsync(cancellationToken)),
        "today" when arguments.Count == 2 => await WriteAsync(_application.GetTodaySummaryAsync(cancellationToken)),
        _ => InvalidCommand()
    };


    private async Task<int> SystemAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.ElementAtOrDefault(1)?.Equals("snapshot", StringComparison.OrdinalIgnoreCase) != true)
        {
            return InvalidCommand();
        }

        if (!TryParseWatchOptions(arguments, 2, out var watch, out var interval))
        {
            return InvalidArguments();
        }

        if (watch)
        {
            return await WatchAsync(
                _application.CaptureSystemSnapshotAsync,
                _output.RenderSystemSnapshot,
                interval,
                cancellationToken);
        }

        var result = await _application.CaptureSystemSnapshotAsync(cancellationToken);
        _output.WriteResult(result, result.Value is null ? null : _output.RenderSystemSnapshot(result.Value));
        return result.Succeeded ? 0 : ExitCodeMapper.Map(result.Code);
    }

    private async Task<int> ScreenshotAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.ElementAtOrDefault(1)?.ToLowerInvariant() is "gallery" or "delete")
        {
            return await ScreenshotMaintenanceAsync(arguments, cancellationToken);
        }

        if (arguments.ElementAtOrDefault(1)?.Equals("capture", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!TryParseOptions(arguments, 2, ["--keep"], ["--mode"], out var options))
            {
                return InvalidArguments();
            }

            return await WriteAsync(_application.CaptureScreenshotAsync(
                new CaptureScreenshotRequest(
                    options.Value("--mode"),
                    options.Contains("--keep"),
                    ScreenshotCaptureOrigins.Manual),
                cancellationToken));
        }

        return arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
        {
            "latest" when arguments.Count == 2 => await WriteAsync(_application.GetLatestScreenshotAsync(cancellationToken)),
            "open-folder" when arguments.Count == 2 => await WriteAsync(_application.OpenScreenshotFolderAsync(cancellationToken)),
            _ => InvalidCommand()
        };
    }

    private async Task<int> AiAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "models" when arguments.Count == 2:
                return await WriteAsync(_application.GetAiModelCatalogAsync(cancellationToken));
            case "pricing" when arguments.Count == 2:
                return await WriteAsync(_application.GetAiPricingOverviewAsync(cancellationToken));
            case "test" when TryParseOptions(arguments, 2, ["--yes"], [], out var testOptions):
                return Confirmed(testOptions)
                    ? await WriteAsync(_application.TestAiConnectionAsync(cancellationToken))
                    : ConfirmationRequired();
            case "reprocess":
                return await AiReprocessAsync(arguments, cancellationToken);
            case "status" when arguments.Count == 2:
                return await WriteAsync(_application.GetAiStatusAsync(cancellationToken));
            case "analyze" when TryParseOptions(arguments, 2, ["--no-capture"], [], out var analyzeOptions):
                return await WriteAsync(_application.AnalyzeCurrentActivityAsync(new AnalyzeCurrentActivityRequest(!analyzeOptions.Contains("--no-capture"), "cli.ai"), cancellationToken));
            case "enable" when arguments.Count == 2:
                return await WriteAsync(_application.SetAiEnabledAsync(true, cancellationToken));
            case "disable" when arguments.Count == 2:
                return await WriteAsync(_application.SetAiEnabledAsync(false, cancellationToken));
            case "configure":
                if (!TryParseOptions(
                        arguments,
                        2,
                        [],
                        ["--provider", "--model", "--endpoint", "--output-detail", "--reasoning-effort"],
                        out var configureOptions))
                {
                    return InvalidArguments();
                }

                var values = new Dictionary<string, string?>();
                AddOption(values, "ai.provider", configureOptions.Value("--provider"));
                AddOption(values, "ai.model", configureOptions.Value("--model"));
                AddOption(values, "ai.endpoint", configureOptions.Value("--endpoint"));
                AddOption(values, "ai.output_detail", configureOptions.Value("--output-detail"));
                AddOption(values, "ai.reasoning_effort", configureOptions.Value("--reasoning-effort"));
                return values.Count == 0 ? InvalidCommand() : await WriteAsync(_application.ConfigureAiAsync(new SettingsPatch(values), cancellationToken));
            case "key" when arguments.ElementAtOrDefault(2)?.Equals("set", StringComparison.OrdinalIgnoreCase) == true:
                if (!TryParseOptions(arguments, 3, [], ["--variable"], out var keyOptions))
                {
                    return InvalidArguments();
                }

                if (Console.IsInputRedirected)
                {
                    return InvalidCommand();
                }
                var variable = keyOptions.Value("--variable");
                if (string.IsNullOrWhiteSpace(variable))
                {
                    var status = await _application.GetAiStatusAsync(cancellationToken);
                    if (!status.Succeeded || status.Value is null)
                    {
                        return WriteResult(status);
                    }

                    variable = status.Value.KeyVariable;
                }
                var keyStrings = new LocalizationService(_options.Language);
                AnsiConsole.MarkupLine(Markup.Escape(keyStrings.Translate("ProviderSetup.Ai.Cost")));
                if (!AnsiConsole.Confirm(Markup.Escape(keyStrings.Translate("ProviderSetup.Continue")), defaultValue: false))
                {
                    return ConfirmationRequired();
                }

                var secret = AnsiConsole.Prompt(new TextPrompt<string>($"[yellow]{Markup.Escape(_output.Text("prompt.apiKey"))}:[/] ").Secret());
                return await WriteAsync(_application.SetAiKeyAsync(variable, secret, cancellationToken));
            default: return InvalidCommand();
        }
    }

    private async Task<int> PrivacyAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "list" when arguments.Count == 2:
                return await WriteAsync(_application.GetPrivacyRulesAsync(cancellationToken));
            case "add" when TryParseOptions(arguments, 2, [], ["--type", "--value"], out var addOptions)
                && addOptions.Value("--type") is { } type
                && addOptions.Value("--value") is { } value:
                return await WriteAsync(_application.AddPrivacyRuleAsync(type, value, cancellationToken));
            case "remove" when TryParseOptions(arguments, 2, [], ["--id"], out var removeOptions)
                && removeOptions.Value("--id") is { } id:
                return await WriteAsync(_application.RemovePrivacyRuleAsync(id, cancellationToken));
            case "test-current" when arguments.Count == 2:
                return await WriteAsync(_application.TestCurrentPrivacyAsync(cancellationToken));
            default:
                return InvalidCommand();
        }
    }

    private async Task<int> RetentionAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "status" when arguments.Count == 2:
                return await WriteAsync(_application.GetRetentionStatusAsync(cancellationToken));
            case "preview" when arguments.Count == 2:
                return await WriteAsync(_application.PreviewRetentionAsync(cancellationToken));
            case "run" when TryParseOptions(arguments, 2, ["--yes"], [], out var options):
                return await WriteAsync(_application.RunRetentionAsync(new RetentionRequest(true, _options.Yes || options.Contains("--yes")), cancellationToken));
            default:
                return InvalidCommand();
        }
    }

    private async Task<int> PluginsAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
    {
        "list" when arguments.Count == 2 => await WriteAsync(_application.GetPluginsAsync(cancellationToken)),
        "show" when arguments.Count == 3 => await WriteAsync(_application.GetPluginAsync(arguments[2], cancellationToken)),
        "enable" when arguments.Count == 3 => await WriteAsync(_application.SetPluginEnabledAsync(arguments[2], true, cancellationToken)),
        "disable" when arguments.Count == 3 => await WriteAsync(_application.SetPluginEnabledAsync(arguments[2], false, cancellationToken)),
        _ => InvalidCommand()
    };

    private async Task<int> ConfigAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "list" when arguments.Count == 2:
                return await ListSettingsAsync(cancellationToken);
            case "get" when arguments.Count == 3:
                return await GetSettingAsync(arguments[2], cancellationToken);
            case "set" when arguments.Count == 4:
                return await SetSettingAsync(arguments[2], arguments[3], cancellationToken);
            case "wizard" when arguments.Count == 2:
                if (Console.IsInputRedirected) return InvalidCommand();
                var language = AnsiConsole.Prompt(new SelectionPrompt<WizardChoice>()
                    .Title($"[cyan]{Markup.Escape(_output.Text("language"))}[/]")
                    .UseConverter(choice => choice.Label)
                    .AddChoices(LanguageWizardChoices(_output)));
                var theme = AnsiConsole.Prompt(new SelectionPrompt<WizardChoice>()
                    .Title($"[cyan]{Markup.Escape(_output.Text("theme"))}[/]")
                    .UseConverter(choice => choice.Label)
                    .AddChoices(ThemeWizardChoices(_output)));
                var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { ["language"] = language.Value, ["theme"] = theme.Value }), cancellationToken);
                return WriteSettingsResult(result, ["language", "theme"]);
            default: return InvalidCommand();
        }
    }

    private async Task<int> ListSettingsAsync(CancellationToken cancellationToken)
    {
        var result = await _application.GetSettingsAsync(cancellationToken);
        return WriteSettingsResult(result, CliSettingsCatalog.Settings.Select(setting => setting.Key));
    }

    private async Task<int> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        if (!CliSettingsCatalog.TryGet(key, out var descriptor) || descriptor is null)
        {
            return InvalidSetting(key);
        }

        var result = await _application.GetSettingsAsync(cancellationToken);
        return WriteSettingsResult(result, [descriptor.Key]);
    }

    private async Task<int> SetSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (!CliSettingsCatalog.TryGet(key, out var descriptor) || descriptor is null)
        {
            return InvalidSetting(key);
        }

        var result = await _application.PatchSettingsAsync(new SettingsPatch(new Dictionary<string, string?> { [descriptor.Key] = value }), cancellationToken);
        return WriteSettingsResult(result, [descriptor.Key]);
    }

    private int WriteSettingsResult(OperationResult<AppSettings> result, IEnumerable<string> keys)
    {
        if (!result.Succeeded || result.Value is null)
        {
            return WriteResult(result);
        }

        var values = keys
            .Select(key => CliSettingsCatalog.TryGet(key, out var descriptor) ? descriptor : null)
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => CliSettingsCatalog.Read(descriptor!, result.Value))
            .ToArray();
        var safeResult = new OperationResult<IReadOnlyList<CliSettingValue>>(result.Succeeded, result.Code, result.MessageKey, values, result.Issues);
        _output.WriteResult(safeResult, _output.RenderSettings(values));
        return safeResult.Succeeded ? 0 : ExitCodeMapper.Map(safeResult.Code);
    }

    private int InvalidSetting(string key) => WriteResult(OperationResult<object>.Failure(
        "command.arguments.invalid",
        "SettingsFieldInvalid",
        new ValidationIssue(key, "not_allowed", "SettingsFieldInvalid")));

    private async Task<int> StartupAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
    {
        "status" when arguments.Count == 2 => await WriteAsync(_application.GetStartupStatusAsync(cancellationToken)),
        "enable" when arguments.Count == 2 => await WriteAsync(_application.SetStartupEnabledAsync(true, cancellationToken)),
        "disable" when arguments.Count == 2 => await WriteAsync(_application.SetStartupEnabledAsync(false, cancellationToken)),
        _ => InvalidCommand()
    };

    private async Task<int> OpenAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.ElementAtOrDefault(1)?.ToLowerInvariant() switch
    {
        "screenshots" when arguments.Count == 2 => await WriteAsync(_application.OpenScreenshotFolderAsync(cancellationToken)),
        "ui" when arguments.Count == 2 => await WriteAsync(_application.OpenUserInterfaceAsync(cancellationToken)),
        _ => InvalidCommand()
    };

    private async Task<int> WriteAsync<T>(Task<OperationResult<T>> task) => WriteResult(await task);

    private async Task<int> WatchAsync<T>(
        Func<CancellationToken, Task<OperationResult<T>>> query,
        Func<T, Spectre.Console.Rendering.IRenderable> render,
        int intervalSeconds,
        CancellationToken cancellationToken)
    {
        if (_options.Format == CliFormat.Json)
        {
            return WriteResult(OperationResult<object>.Failure("command.arguments.invalid", "WatchDoesNotSupportJson", new ValidationIssue("watch", "not_supported", "WatchDoesNotSupportJson")));
        }

        var initial = await query(cancellationToken);
        if (!initial.Succeeded || initial.Value is null)
        {
            _output.WriteResult(initial);
            return initial.Succeeded ? 0 : ExitCodeMapper.Map(initial.Code);
        }

        if (_options.Format != CliFormat.Rich)
        {
            do
            {
                _output.WriteResult(initial);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                var denied = await CheckCliAccessAsync(cancellationToken);
                if (denied is not null) return denied.Value;
                initial = await query(cancellationToken);
                if (!initial.Succeeded || initial.Value is null)
                {
                    _output.WriteResult(initial);
                    return initial.Succeeded ? 0 : ExitCodeMapper.Map(initial.Code);
                }
            } while (!cancellationToken.IsCancellationRequested);

            return 130;
        }

        var code = 0;
        await AnsiConsole.Live(render(initial.Value)).StartAsync(async context =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                var denied = await CheckCliAccessAsync(cancellationToken);
                if (denied is not null)
                {
                    code = denied.Value;
                    break;
                }
                var next = await query(cancellationToken);
                if (!next.Succeeded || next.Value is null)
                {
                    code = next.Succeeded ? 0 : ExitCodeMapper.Map(next.Code);
                    context.UpdateTarget(new Spectre.Console.Panel(
                        new Spectre.Console.Markup(Spectre.Console.Markup.Escape(_output.ResultText(next.MessageKey, next.Succeeded))))
                        .BorderColor(Spectre.Console.Color.IndianRed));
                    break;
                }

                context.UpdateTarget(render(next.Value));
            }
        });

        return cancellationToken.IsCancellationRequested ? 130 : code;
    }

    private int WriteResult<T>(OperationResult<T> result)
    {
        _output.WriteResult(result);
        return result.Succeeded ? 0 : ExitCodeMapper.Map(result.Code);
    }

    private int InvalidCommand()
    {
        _output.WriteResult(OperationResult<object>.Failure("command.invalid", "CommandInvalid"));
        return 2;
    }

    private int InvalidArguments(string field = "arguments") => WriteResult(OperationResult<object>.Failure(
        "command.arguments.invalid",
        "CommandArgumentsInvalid",
        new ValidationIssue(field, "invalid", "CommandArgumentsInvalid")));

    private int WriteHelp(string? topic)
    {
        if (_output.WriteHelp(topic))
        {
            return 0;
        }

        return InvalidCommand();
    }

    private static bool TryParseWatchOptions(
        IReadOnlyList<string> arguments,
        int startIndex,
        out bool watch,
        out int interval)
    {
        watch = false;
        interval = 2;
        if (!TryParseOptions(arguments, startIndex, ["--watch"], ["--interval"], out var options))
        {
            return false;
        }

        watch = options.Contains("--watch");
        var rawInterval = options.Value("--interval");
        if (rawInterval is null)
        {
            return true;
        }

        return watch
            && int.TryParse(rawInterval, out interval)
            && interval is >= 1 and <= 60;
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> arguments,
        int startIndex,
        IReadOnlyList<string> flagNames,
        IReadOnlyList<string> valueNames,
        out ParsedOptions options)
    {
        var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = startIndex; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (flagNames.Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                if (!parsed.TryAdd(argument, null))
                {
                    options = ParsedOptions.Empty;
                    return false;
                }

                continue;
            }

            if (!valueNames.Contains(argument, StringComparer.OrdinalIgnoreCase)
                || !parsed.TryAdd(argument, null)
                || index + 1 >= arguments.Count
                || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options = ParsedOptions.Empty;
                return false;
            }

            parsed[argument] = arguments[++index];
        }

        options = new ParsedOptions(parsed);
        return true;
    }

    private static void AddOption(IDictionary<string, string?> values, string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) values[key] = value; }

    internal static bool TryTokenize(string line, out IReadOnlyList<string> tokens)
    {
        var parsed = new List<string>();
        var builder = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in line.Trim())
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted) { if (builder.Length > 0) { parsed.Add(builder.ToString()); builder.Clear(); } continue; }
            builder.Append(character);
        }

        if (quoted)
        {
            tokens = [];
            return false;
        }

        if (builder.Length > 0) parsed.Add(builder.ToString());
        tokens = parsed;
        return true;
    }

    internal static IReadOnlyList<WizardChoice> LanguageWizardChoices(CliOutput output) =>
        CliOptions.SupportedLanguages
            .Select(value => new WizardChoice(
                value,
                value.Equals("system", StringComparison.Ordinal)
                    ? output.Text("choice.language.system")
                    : value))
            .ToArray();

    internal static IReadOnlyList<WizardChoice> ThemeWizardChoices(CliOutput output) =>
    [
        new("system", output.Text("choice.theme.system")),
        new("light", output.Text("choice.theme.light")),
        new("dark", output.Text("choice.theme.dark"))
    ];

    internal sealed record WizardChoice(string Value, string Label);
    private sealed record ParsedOptions(IReadOnlyDictionary<string, string?> Values)
    {
        internal static ParsedOptions Empty { get; } = new(new Dictionary<string, string?>());

        internal bool Contains(string name) => Values.ContainsKey(name);

        internal string? Value(string name) => Values.TryGetValue(name, out var value) ? value : null;
    }

    private sealed record ShellAction(string Id, string Label, IReadOnlyList<string>? Command = null);
}
