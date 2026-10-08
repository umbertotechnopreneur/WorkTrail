// SPDX-License-Identifier: MIT

using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Identifies the presentation/runtime mode selected before XAML activation.</summary>
public enum LaunchMode
{
    /// <summary>Starts the standard WinUI player.</summary>
    Ui,
    /// <summary>Starts the Spectre.Console CLI frontend.</summary>
    Cli,
    /// <summary>Starts the invisible local runtime host.</summary>
    Background,
    /// <summary>Prints launch help without opening a window.</summary>
    Help,
    /// <summary>Prints product and protocol versions without opening a window.</summary>
    Version
}

/// <summary>Contains bootstrap-only switches that are evaluated before any view is created.</summary>
public sealed record LaunchOptions(
    LaunchMode Mode,
    bool StartTracking,
    bool Paused,
    string? Language,
    string? Theme,
    string? Position,
    bool SafeMode,
    bool StartWithWindows,
    bool NoSplash,
    IReadOnlyList<string> RemainingArguments)
{
    /// <summary>Identifies a recovery launch requested by the process memory guard.</summary>
    public bool MemoryRecovery { get; init; }

    /// <summary>Parses supported bootstrap switches without constructing services or windows.</summary>
    public static LaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var mode = LaunchMode.Ui;
        var cliRequested = arguments.Any(argument => argument is "-cli" or "--cli");
        var startTracking = false;
        var paused = false;
        string? language = null;
        string? theme = null;
        string? position = null;
        var safeMode = false;
        var startWithWindows = false;
        var noSplash = false;
        var memoryRecovery = false;
        var remaining = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var value = arguments[index];
            switch (value)
            {
                case "-cli":
                case "--cli": mode = LaunchMode.Cli; break;
                case "reports" when !cliRequested:
                    // Removed entry points must fail explicitly instead of opening an unrelated window.
                    throw new ArgumentException("Unsupported launch argument 'reports'.", nameof(arguments));
                case "--background": mode = LaunchMode.Background; break;
                case "--ui": mode = LaunchMode.Ui; break;
                case "-h":
                case "--help": mode = LaunchMode.Help; break;
                case "--version": mode = LaunchMode.Version; break;
                case "--start-tracking": startTracking = true; break;
                case "--paused": paused = true; break;
                case "--safe-mode": safeMode = true; break;
                case "--start-with-windows": startWithWindows = true; break;
                case "--no-splash": noSplash = true; break;
                case "--memory-recovery": memoryRecovery = true; break;
                case "--language" when index + 1 < arguments.Count:
                    language = ProductLanguageCatalog.CanonicalUiChoice(arguments[++index])
                        ?? throw new ArgumentException($"Unsupported WorkTrail language '{arguments[index]}'.", nameof(arguments));
                    break;
                case "--language":
                    throw new ArgumentException("--language requires a value.", nameof(arguments));
                case "--theme" when index + 1 < arguments.Count: theme = arguments[++index]; break;
                case "--position" when index + 1 < arguments.Count: position = arguments[++index]; break;
                default: remaining.Add(value); break;
            }
        }

        // The explicit CLI frontend always wins, regardless of argument order; its command tokens remain untouched.
        if (cliRequested)
        {
            mode = LaunchMode.Cli;
        }

        return new LaunchOptions(mode, startTracking, paused, language, theme, position, safeMode, startWithWindows, noSplash, remaining)
        {
            MemoryRecovery = memoryRecovery
        };
    }
}
