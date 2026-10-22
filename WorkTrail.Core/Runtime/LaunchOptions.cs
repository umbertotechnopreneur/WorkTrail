// SPDX-License-Identifier: MIT
using WorkTrail.Services;
namespace WorkTrail.Runtime;

/// <summary>Identifies a visible launcher or a hidden Windows startup request.</summary>
public enum LaunchMode
{
    Ui,
    Background
}

/// <summary>Contains widget launch preferences parsed before XAML activation.</summary>
public sealed record LaunchOptions(LaunchMode Mode, string? Language, string? Theme, bool StartWithWindows)
{

    #region Methods and properties
    /// <summary>Parses the supported desktop launcher switches.</summary>
    public static LaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var mode = LaunchMode.Ui;
        string? language = null;
        string? theme = null;
        var startup = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--ui":
                    mode = LaunchMode.Ui;
                    break;
                case "--background":
                    mode = LaunchMode.Background;
                    break;
                case "--start-with-windows":
                    startup = true;
                    break;
                case "--language" when index + 1 < arguments.Count:
                    language = ProductLanguageCatalog.CanonicalUiChoice(arguments[++index]) ?? throw new ArgumentException("Unsupported language.");
                    break;
                case "--theme" when index + 1 < arguments.Count:
                    theme = arguments[++index];
                    if (theme is not ("system" or "light" or "dark"))
                    {
                        throw new ArgumentException("Unsupported theme.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unsupported launch argument '{arguments[index]}'.");
            }
        }
        return new LaunchOptions(mode, language, theme, startup);
    }

    #endregion

}
