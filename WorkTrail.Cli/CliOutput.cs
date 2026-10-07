// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Cli;

/// <summary>Renders application DTOs without querying infrastructure or changing application state.</summary>
/// <remarks>Initializes output rendering for one invocation.</remarks>
public sealed class CliOutput(CliOptions options)
{
    private readonly CliOptions _options = options;
    private readonly CultureInfo _culture = CliStrings.GetCulture(options.Language);
    private bool _jsonWritten;

    /// <summary>Writes a result using the selected output contract.</summary>
    public void WriteResult<T>(OperationResult<T> result, IRenderable? richContent = null)
    {
        if (_options.Format == CliFormat.Json)
        {
            if (_jsonWritten)
            {
                return;
            }

            _jsonWritten = true;
            Console.Out.Write(SerializeResult(result));
            Console.Out.WriteLine();
            return;
        }

        if (_options.Quiet && result.Succeeded)
        {
            return;
        }

        if (_options.Format == CliFormat.Plain)
        {
            Console.Out.WriteLine($"{(result.Succeeded ? Localize("ok") : Localize("error"))} {result.Code}: {ResultText(result.MessageKey, result.Succeeded)}");
            if (result.Value is not null && !_options.Quiet)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            }
            return;
        }

        AnsiConsole.Write(RenderResult(result, richContent));
    }

    /// <summary>Writes general or command-specific CLI help without querying the runtime.</summary>
    public bool WriteHelp(string? topic = null)
    {
        if (!CliCommandCatalog.TryGet(topic, out var command))
        {
            return false;
        }

        var text = command is null ? BuildGeneralHelp() : BuildCommandHelp(command);
        if (_options.Format == CliFormat.Json)
        {
            object value = command is null
                ? new
                {
                    usage = "worktrail.exe -cli /command [arguments] [global options]",
                    slashPrefixOptional = true,
                    commands = CliCommandCatalog.Commands.Select(item => new { command = item.Name, summary = Localize(item.SummaryKey), aliases = item.Aliases }),
                    shortcuts = CliCommandCatalog.Shortcuts.Select(item => new { option = item.Option, command = item.Command, summary = Localize(item.SummaryKey) }),
                    globalOptions = GlobalOptions
                }
                : new
                {
                    command = command.Name,
                    summary = Localize(command.SummaryKey),
                    usage = command.Usage,
                    details = command.DetailKeys.Select(LocalizeDetail),
                    aliases = command.Aliases
                };
            WriteResult(OperationResult<object>.Success("help.displayed", "HelpDisplayed", value));
        }
        else if (_options.Format == CliFormat.Plain)
        {
            Console.Out.WriteLine(text);
        }
        else
        {
            AnsiConsole.Write(command is null ? RenderGeneralHelp() : RenderCommandHelp(command));
        }

        return true;
    }

    /// <summary>Writes a diagnostic warning only when a human is reading the terminal.</summary>
    public void WriteDiagnostic(string message)
    {
        if (_options.Format == CliFormat.Rich)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(Localize("warning"))}:[/] {Markup.Escape(message)}");
        }
        else if (_options.Format != CliFormat.Json && _options.Verbose)
        {
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>Renders current dashboard data in a deliberately compact panel and table.</summary>
    public IRenderable RenderDashboard(DashboardState dashboard)
    {
        var table = new Table().Border(TableBorder.None).AddColumn(Localize("metric")).AddColumn(Localize("value"));
        table.AddRow(
            Localize("state"),
            Markup.Escape(Localize(dashboard.IsTracking ? "shell.tracking" : "shell.paused")));
        table.AddRow(Localize("context"), Markup.Escape(dashboard.CurrentContext));
        table.AddRow(Localize("keys"), dashboard.TotalKeyPresses.ToString("N0", _culture));
        table.AddRow(Localize("clicks"), dashboard.TotalMouseClicks.ToString("N0", _culture));
        table.AddRow(Localize("activeSeconds"), dashboard.ActiveSeconds.ToString("N0", _culture));
        table.AddRow(Localize("intensity"), dashboard.Intensity.ToString("F0", _culture) + "%");
        return new Panel(table).Header($"[bold coral1]{Markup.Escape(Localize("statusTitle"))}[/]").BorderColor(Color.Teal);
    }

    /// <summary>Writes the branded header used by the interactive command center.</summary>
    public void WriteShellHeader()
    {
        var product = new Rows(
            new FigletText("WorkTrail").Color(Color.Teal),
            new Markup($"[grey70]{Markup.Escape(Localize("shell.tagline"))}[/]"));
        AnsiConsole.Write(new VibeWareBrand(AnsiConsole.Console, product));
        AnsiConsole.WriteLine();
    }

    /// <summary>Renders a compact live overview for the interactive command center.</summary>
    public IRenderable RenderShellDashboard(DashboardState dashboard, AiStatus? aiStatus)
    {
        var tracking = dashboard.IsTracking
            ? $"[green]● {Markup.Escape(Localize("shell.tracking"))}[/]"
            : $"[yellow]● {Markup.Escape(Localize("shell.paused"))}[/]";
        var ai = aiStatus is null
            ? $"[grey]{Markup.Escape(Localize("shell.unavailable"))}[/]"
            : aiStatus.Enabled
                ? $"[green]{Markup.Escape(Localize("shell.enabled"))}[/] [grey]· {Markup.Escape(aiStatus.Provider)} / {Markup.Escape(aiStatus.Model)}[/]"
                : $"[grey]{Markup.Escape(Localize("shell.disabled"))}[/]";
        var metrics = new Table().Border(TableBorder.None)
            .AddColumn(new TableColumn($"[grey]{Markup.Escape(Localize("shell.signal"))}[/]"))
            .AddColumn(new TableColumn($"[grey]{Markup.Escape(Localize("shell.today"))}[/]").RightAligned());
        metrics.AddRow(Localize("keys"), dashboard.TotalKeyPresses.ToString("N0", _culture));
        metrics.AddRow(Localize("clicks"), dashboard.TotalMouseClicks.ToString("N0", _culture));
        metrics.AddRow(Localize("activeSeconds"), TimeSpan.FromSeconds(dashboard.ActiveSeconds).ToString("hh\\:mm\\:ss"));
        metrics.AddRow(Localize("intensity"), dashboard.Intensity.ToString("F0", _culture) + "%");

        var content = new Rows(
            new Markup($"[bold]{tracking}[/] [grey]· {Markup.Escape(Localize("shell.ai"))}: {ai}[/]\n[grey]{Markup.Escape(Localize("context"))}:[/] {Markup.Escape(dashboard.CurrentContext)}"),
            new Rule().RuleStyle("grey").LeftJustified(),
            metrics);
        return new Panel(content)
            .Header($"[bold cyan]{Markup.Escape(Localize("shell.liveWorkspace"))}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(dashboard.IsTracking ? Color.Teal : Color.Grey);
    }

    /// <summary>Renders a system snapshot in a stable table.</summary>
    public IRenderable RenderSystemSnapshot(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var strings = new LocalizationService(_options.Language);
        var table = new Table().Border(TableBorder.Rounded).AddColumn(Localize("metric")).AddColumn(Localize("value"));
        table.AddRow(Localize("state"), Markup.Escape(snapshot.Status));
        table.AddRow(Markup.Escape(strings.Format("Hardware.CollectedAt", snapshot.Timestamp.ToLocalTime().ToString("G", _culture))), string.Empty);
        table.AddRow(Markup.Escape(strings.Translate("Hardware.Advanced.Label")), Markup.Escape(snapshot.DriverStatus));
        if (snapshot.ErrorCode is { } errorCode)
        {
            table.AddRow(Localize("error"), Markup.Escape(errorCode));
        }
        foreach (var device in snapshot.Devices)
        {
            table.AddRow(Markup.Escape($"{device.Name} ({device.Kind})"),
                Markup.Escape(strings.Format("Hardware.DeviceUpdated", device.SampledAt.ToLocalTime().ToString("G", _culture))));
            foreach (var sensor in device.Sensors)
            {
                var value = sensor.Value is { } reading ? $"{reading.ToString("0.##", _culture)} {sensor.Unit}" : Localize("notAvailable");
                table.AddRow(Markup.Escape($"  {sensor.Name} ({sensor.Kind})"), Markup.Escape(value));
            }
        }
        if (!snapshot.Devices.Any(device => device.Kind == "Battery"))
        {
            table.AddRow(Markup.Escape(strings.Translate("Hardware.Category.Battery")), Localize("notAvailable"));
        }
        table.AddRow(Markup.Escape(strings.Translate("Hardware.PowerNote")), string.Empty);
        return new Panel(table).Header($"[bold cyan]{Markup.Escape(Localize("systemSnapshot"))}[/]");
    }

    /// <summary>Renders the public settings catalog and current typed values without exposing internal fields.</summary>
    internal IRenderable RenderSettings(IReadOnlyList<CliSettingValue> settings)
    {
        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn(Localize("setting"))
            .AddColumn(Localize("value"))
            .AddColumn(Localize("type"))
            .AddColumn(Localize("allowed"))
            .AddColumn(Localize("restart"));
        foreach (var setting in settings)
        {
            var value = setting.Value switch
            {
                null => Localize("notAvailable"),
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => setting.Value.ToString() ?? Localize("notAvailable")
            };
            table.AddRow(
                Markup.Escape(setting.Key),
                Markup.Escape(value),
                Markup.Escape(setting.ValueType),
                Markup.Escape(setting.AllowedValues.Count == 0 ? "—" : string.Join(" | ", setting.AllowedValues)),
                setting.RequiresRestart ? Localize("yes") : Localize("no"));
        }

        return new Panel(table).Header($"[bold cyan]{Markup.Escape(Localize("settings"))}[/]").BorderColor(Color.Teal);
    }

    /// <summary>Renders a generic application result, including its value when no specialized widget was supplied.</summary>
    internal IRenderable RenderResult<T>(OperationResult<T> result, IRenderable? richContent = null)
    {
        var icon = result.Succeeded ? "[green]✓[/]" : "[red]✗[/]";
        var valueText = result.Value is null
            ? string.Empty
            : "\n\n" + JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var body = richContent ?? new Markup($"{icon} {Markup.Escape(ResultText(result.MessageKey, result.Succeeded))}\n[grey70]{Markup.Escape(result.Code)}[/]{Markup.Escape(valueText)}");
        return new Panel(body).BorderColor(result.Succeeded ? Color.Teal : Color.IndianRed);
    }

    /// <summary>Serializes the stable automation result envelope independently from display localization.</summary>
    internal static string SerializeResult<T>(OperationResult<T> result) =>
        JsonSerializer.Serialize(
            new { succeeded = result.Succeeded, code = result.Code, messageKey = result.MessageKey, value = result.Value, issues = result.Issues },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    internal string Text(string key) => Localize(key);

    internal string ResultText(string messageKey, bool succeeded) =>
        CliStrings.GetResult(_options.Language, messageKey, succeeded);

    private string Localize(string key) => CliStrings.Get(_options.Language, key);

    private string LocalizeDetail(string key) => key == "detail.configWritable"
        ? CliStrings.Format(_options.Language, key, CliSettingsCatalog.HelpValueSummary)
        : Localize(key);

    private static readonly (string Option, string DescriptionKey)[] GlobalOptionDetails =
    [
        ("--format <rich|plain|json>", "option.format"),
        ("--json", "option.json"),
        ("--language <system|en-US|it-IT|fr-FR|de-DE|es-ES|zh-Hans|vi-VN|ko-KR|pt-PT|pt-BR>", "option.language"),
        ("--quiet", "option.quiet"),
        ("--yes", "option.yes"),
        ("--timeout <1-300>", "option.timeout"),
        ("--verbose", "option.verbose")
    ];

    private static readonly string[] GlobalOptions = GlobalOptionDetails.Select(item => item.Option).ToArray();

    private string BuildGeneralHelp()
    {
        var lines = new List<string>
        {
            "WorkTrail CLI",
            string.Empty,
            $"{Localize("usage")}: worktrail.exe -cli /command [arguments] [global options]",
            Localize("help.slashOptional"),
            string.Empty,
            $"{Localize("commands")}:"
        };
        lines.AddRange(CliCommandCatalog.Commands.Select(command => $"  /{command.Name,-12} {Localize(command.SummaryKey)}"));
        lines.AddRange([
            string.Empty,
            Localize("help.command"),
            string.Empty,
            Localize("help.quickSwitches") + ": " + string.Join(", ", CliCommandCatalog.Shortcuts.Select(shortcut => shortcut.Option)),
            string.Empty,
            $"{Localize("globalOptions")}: {string.Join(", ", GlobalOptions)}"
        ]);
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildCommandHelp(CliCommandHelp command)
    {
        var lines = new List<string>
        {
            $"WorkTrail CLI /{command.Name}",
            Localize(command.SummaryKey),
            string.Empty,
            $"{Localize("usage")}:"
        };
        lines.AddRange(command.Usage.Select(usage => $"  worktrail.exe -cli {usage}"));
        if (command.Aliases.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Localize("aliases") + ": " + string.Join(", ", command.Aliases.Select(alias => $"/{alias}")));
        }
        if (command.DetailKeys.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(command.DetailKeys.Select(LocalizeDetail));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private IRenderable RenderGeneralHelp()
    {
        var commands = new Table().Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("command"))}[/]"))
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("whatItDoes"))}[/]"));
        foreach (var command in CliCommandCatalog.Commands)
        {
            commands.AddRow($"[teal]/{Markup.Escape(command.Name)}[/]", Markup.Escape(Localize(command.SummaryKey)));
        }

        var shortcuts = new Table().Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("quickSwitch"))}[/]"))
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("action"))}[/]"));
        foreach (var shortcut in CliCommandCatalog.Shortcuts)
        {
            shortcuts.AddRow($"[teal]{Markup.Escape(shortcut.Option)}[/]", Markup.Escape(Localize(shortcut.SummaryKey)));
        }

        var globalOptions = new Table().Border(TableBorder.Rounded)
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("option"))}[/]"))
            .AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("purpose"))}[/]"));
        foreach (var option in GlobalOptionDetails)
        {
            globalOptions.AddRow($"[teal]{Markup.Escape(option.Option)}[/]", Markup.Escape(Localize(option.DescriptionKey)));
        }

        return new Rows(
            new VibeWareBrand(AnsiConsole.Console, new Rows(
                new Rule("[bold cyan]WorkTrail CLI[/]").LeftJustified(),
                new Markup($"[grey70]{Markup.Escape(Localize("help.tagline"))}[/]"))),
            new Markup($"[grey]{Markup.Escape(Localize("usage"))}:[/] [teal]worktrail.exe -cli /command [arguments] [global options][/][grey]  ({Markup.Escape(Localize("help.slashOptional"))})[/]"),
            new Panel(commands).Header($"[bold]{Markup.Escape(Localize("commands"))}[/]").BorderColor(Color.Teal),
            new Panel(shortcuts).Header($"[bold]{Markup.Escape(Localize("help.quickSwitches"))}[/]").BorderColor(Color.Teal),
            new Panel(globalOptions).Header($"[bold]{Markup.Escape(Localize("globalOptions"))}[/]").BorderColor(Color.Grey),
            new Markup($"[grey]{Markup.Escape(Localize("help.command"))}[/]"));
    }

    private IRenderable RenderCommandHelp(CliCommandHelp command)
    {
        var usage = new Table().Border(TableBorder.Rounded).AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(Localize("usage"))}[/]"));
        foreach (var item in command.Usage)
        {
            usage.AddRow($"[teal]worktrail.exe -cli {Markup.Escape(item)}[/]");
        }

        var content = new List<IRenderable>
        {
            new Markup($"[bold]{Markup.Escape(Localize(command.SummaryKey))}[/]"),
            usage
        };
        if (command.Aliases.Count > 0)
        {
            content.Add(new Markup($"[grey]{Markup.Escape(Localize("aliases"))}:[/] {Markup.Escape(string.Join(", ", command.Aliases.Select(alias => "/" + alias)))}"));
        }
        content.AddRange(command.DetailKeys.Select(LocalizeDetail).Select(detail => new Markup($"[grey]{Markup.Escape(detail)}[/]")));
        return new Panel(new Rows([.. content])).Header($"[bold cyan]/{Markup.Escape(command.Name)}[/]").BorderColor(Color.Teal);
    }
}
