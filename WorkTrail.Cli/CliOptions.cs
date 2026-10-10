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


using WorkTrail.Services;

namespace WorkTrail.Cli;

/// <summary>Defines the supported automation-safe output modes.</summary>
public enum CliFormat
{
    /// <summary>Uses rich Spectre.Console widgets on an interactive terminal.</summary>
    Rich,
    /// <summary>Uses simple text with no ANSI control sequences.</summary>
    Plain,
    /// <summary>Writes one machine-readable JSON document.</summary>
    Json
}

/// <summary>Stores parsed global CLI options independently from command-specific arguments.</summary>
public sealed record CliOptions(
    CliFormat Format,
    string Language,
    bool Quiet,
    bool Yes,
    int TimeoutSeconds,
    bool Verbose,
    IReadOnlyList<string> CommandArguments)
{
    /// <summary>Gets the canonical locale choices accepted by the CLI.</summary>
    public static IReadOnlyList<string> SupportedLanguages => ProductLanguageCatalog.UiChoices;

    /// <summary>Parses global flags and leaves command arguments intact.</summary>
    public static CliOptions Parse(IReadOnlyList<string> arguments, bool redirected)
    {
        var format = redirected ? CliFormat.Plain : CliFormat.Rich;
        var language = "system";
        var quiet = false;
        var yes = false;
        var timeout = 5;
        var verbose = false;
        var remaining = new List<string>();

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--format":
                    format = ParseFormat(ReadRequiredValue(arguments, ref index, "format"));
                    break;
                case "--json": format = CliFormat.Json; break;
                case "--language": language = ReadRequiredValue(arguments, ref index, "language"); break;
                case "--quiet": quiet = true; break;
                case "--yes": yes = true; break;
                case "--timeout":
                    var timeoutValue = ReadRequiredValue(arguments, ref index, "timeout");
                    if (!int.TryParse(timeoutValue, out var parsed) || parsed is <= 0 or > 300)
                    {
                        throw new ArgumentException("timeout must be between 1 and 300 seconds");
                    }
                    timeout = parsed;
                    break;
                case "--verbose": verbose = true; break;
                default: remaining.Add(argument); break;
            }
        }

        var canonicalLanguage = ProductLanguageCatalog.CanonicalUiChoice(language);
        if (canonicalLanguage is null)
        {
            throw new ArgumentException($"Unsupported CLI locale '{language}'. Use one of: {string.Join(", ", SupportedLanguages)}.");
        }

        return new CliOptions(format, canonicalLanguage, quiet, yes, timeout, verbose, remaining);
    }

    private static string ReadRequiredValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (index + 1 < arguments.Count)
        {
            return arguments[++index];
        }

        throw new ArgumentException($"{option} requires a value");
    }

    private static CliFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "rich" => CliFormat.Rich,
        "plain" => CliFormat.Plain,
        "json" => CliFormat.Json,
        _ => throw new ArgumentException("format must be rich, plain, or json")
    };
}

/// <summary>Maps stable application result codes to documented process exit codes.</summary>
public static class ExitCodeMapper
{
    /// <summary>Maps a stable result code to one automation-safe exit code.</summary>
    public static int Map(string code) => code switch
    {
        "operation.cancelled" => 130,
        "cli.premium.required" => 11,
        "command.invalid" or "command.arguments.invalid" => 2,
        var value when value.Contains("validation", StringComparison.Ordinal) || value.EndsWith(".invalid", StringComparison.Ordinal) || value.EndsWith(".required", StringComparison.Ordinal) => 3,
        "runtime.unavailable" => 4,
        "privacy.blocked" => 5,
        "ai.disabled" or "ai.configuration.invalid" => 6,
        "ai.cost_guardrail" => 7,
        "ipc.protocol.unsupported" => 9,
        var value when value.EndsWith(".failed", StringComparison.Ordinal) => 8,
        _ => 10
    };
}
