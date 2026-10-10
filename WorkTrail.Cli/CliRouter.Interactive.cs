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

namespace WorkTrail.Cli;

/// <summary>Provides the CLI health readout and interactive shell without adding application behavior.</summary>
public sealed partial class CliRouter
{
    private async Task<int> DoctorAsync(CancellationToken cancellationToken)
    {
        var healthTask = _application.GetRuntimeHealthAsync(cancellationToken);
        var dashboardTask = _application.GetDashboardAsync(cancellationToken);
        var aiTask = _application.GetAiStatusAsync(cancellationToken);
        var retentionTask = _application.GetRetentionStatusAsync(cancellationToken);
        var startupTask = _application.GetStartupStatusAsync(cancellationToken);
        var pluginsTask = _application.GetPluginsAsync(cancellationToken);
        await Task.WhenAll(healthTask, dashboardTask, aiTask, retentionTask, startupTask, pluginsTask);

        var health = await healthTask;
        var dashboard = await dashboardTask;
        var ai = await aiTask;
        var retention = await retentionTask;
        var startup = await startupTask;
        var plugins = await pluginsTask;
        var succeeded = health.Succeeded && dashboard.Succeeded && ai.Succeeded && retention.Succeeded && startup.Succeeded && plugins.Succeeded;
        var issues = new[] { health.Issues, dashboard.Issues, ai.Issues, retention.Issues, startup.Issues, plugins.Issues }
            .SelectMany(group => group)
            .ToArray();
        var value = new
        {
            runtime = health.Value,
            ai = ai.Value,
            tracking = dashboard.Value is null ? null : new { dashboard.Value.IsTracking, dashboard.Value.StatusLabel, dashboard.Value.LastSampleTimestamp },
            retention = retention.Value is null ? null : new { retention.Value.DataRetentionDays, retention.Value.ScreenshotRetentionDays },
            startupEnabled = startup.Value,
            plugins = plugins.Value?.Select(plugin => new { plugin.Id, plugin.Enabled }).ToArray()
        };
        var result = new OperationResult<object>(succeeded, succeeded ? "doctor.healthy" : "doctor.partial", succeeded ? "DoctorHealthy" : "DoctorPartial", value, issues);
        _output.WriteResult(result);
        return result.Succeeded ? 0 : 10;
    }

    private async Task<int> RunShellAsync(CancellationToken cancellationToken)
    {
        if (_options.Format != CliFormat.Rich || Console.IsInputRedirected)
        {
            _output.WriteHelp();
            return 0;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var denied = await CheckCliAccessAsync(cancellationToken);
            if (denied is not null) return denied.Value;

            AnsiConsole.Clear();
            _output.WriteShellHeader();

            var dashboardTask = _application.GetDashboardAsync(cancellationToken);
            var aiStatusTask = _application.GetAiStatusAsync(cancellationToken);
            await Task.WhenAll(dashboardTask, aiStatusTask);
            var dashboard = await dashboardTask;
            var aiStatus = await aiStatusTask;
            if (dashboard.Succeeded && dashboard.Value is not null)
            {
                AnsiConsole.Write(_output.RenderShellDashboard(dashboard.Value, aiStatus.Succeeded ? aiStatus.Value : null));
            }
            else
            {
                _output.WriteResult(dashboard);
            }

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<ShellAction>()
                    .Title($"[bold cyan]{Markup.Escape(_output.Text("shell.chooseAction"))}[/]")
                    .HighlightStyle(new Style(Color.Teal))
                    .UseConverter(item => item.Label)
                    .PageSize(12)
                    .AddChoices(BuildShellActions(dashboard.Value, aiStatus.Value)));
            if (action.Id == "exit")
            {
                return 0;
            }
            if (action.Id == "refresh")
            {
                continue;
            }

            if (action.Id == "command")
            {
                var line = AnsiConsole.Prompt(new TextPrompt<string>("[teal]worktrail>[/] ").AllowEmpty());
                if (!TryTokenize(line, out var parsedTokens))
                {
                    InvalidArguments();
                }
                else
                {
                    var tokens = CliCommandCatalog.Normalize(parsedTokens);
                    if (tokens.Count > 0)
                    {
                        if (CliCommandCatalog.TryExpandShortcut(tokens, out var expanded))
                        {
                            await DispatchAsync(expanded, cancellationToken);
                        }
                        else
                        {
                            InvalidCommand();
                        }
                    }
                }
            }
            else if (action.Id == "help")
            {
                await DispatchAsync(["help"], cancellationToken);
            }
            else if (action.Command is not null)
            {
                await DispatchAsync(action.Command, cancellationToken);
            }

            AnsiConsole.Prompt(new TextPrompt<string>($"[grey]{Markup.Escape(_output.Text("shell.return"))}[/]").AllowEmpty());
        }

        return 130;
    }

    private IReadOnlyList<ShellAction> BuildShellActions(DashboardState? dashboard, AiStatus? aiStatus)
    {
        var trackingAction = dashboard?.IsTracking == true
            ? new ShellAction("pause", _output.Text("action.pause"), ["tracking", "pause"])
            : new ShellAction("start", _output.Text("action.start"), ["tracking", "start"]);
        var aiAction = aiStatus?.Enabled == true
            ? new ShellAction("ai-off", _output.Text("action.aiOff"), ["ai", "disable"])
            : new ShellAction("ai-on", _output.Text("action.aiOn"), ["ai", "enable"]);
        return
        [
            new ShellAction("refresh", _output.Text("action.refresh")),
            trackingAction,
            new ShellAction("toggle", _output.Text("action.toggle"), ["tracking", "toggle"]),
            aiAction,
            new ShellAction("capture", _output.Text("action.capture"), ["screenshot", "capture"]),
            new ShellAction("doctor", _output.Text("action.doctor"), ["doctor"]),
            new ShellAction("settings", _output.Text("action.settings"), ["config", "wizard"]),
            new ShellAction("open", _output.Text("action.open"), ["open", "ui"]),
            new ShellAction("help", _output.Text("action.help")),
            new ShellAction("command", _output.Text("action.command")),
            new ShellAction("exit", _output.Text("action.exit"))
        ];
    }
}
