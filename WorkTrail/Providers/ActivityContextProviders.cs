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

namespace WorkTrail.Providers;

public sealed record ForegroundWindowInfo(string ProcessName, string WindowTitle);
public sealed record ActivityContext(string Application, string Context, IReadOnlyDictionary<string, string>? Attributes = null);

public interface IActivityContextProvider
{
    /// <summary>
    /// Checks whether the provider can resolve details for the foreground window.
    /// </summary>
    bool CanHandle(ForegroundWindowInfo window);

    /// <summary>
    /// Resolves application/context metadata.
    /// </summary>
    ActivityContext Resolve(ForegroundWindowInfo window);
}

/// <summary>
/// Routes windows to specialized context providers then falls back to generic info.
/// </summary>
public sealed class ActivityContextProviderRegistry
{
    private readonly IReadOnlyList<IActivityContextProvider> _providers =
    [
        new MicrosoftOfficeContextProvider(),
        new AdobeCreativeContextProvider(),
        new KnownApplicationContextProvider(),
        new GenericContextProvider()
    ];

    /// <summary>
    /// Resolves contextual info using the first matching provider.
    /// </summary>
    public ActivityContext Resolve(ForegroundWindowInfo window, AppSettings? settings = null)
    {
        if (settings is not null && !IsDetailEnabled(window.ProcessName, settings))
        {
            // Disabled collectors retain only process identity; the generic provider must not restore titles.
            return new ActivityContext(window.ProcessName, string.Empty);
        }

        foreach (var provider in _providers)
        {
            if (provider.CanHandle(window))
            {
                return provider.Resolve(window);
            }
        }

        return new ActivityContext(window.ProcessName, window.WindowTitle);
    }

    /// <summary>Returns whether the current preferences permit collecting details for a process.</summary>
    public static bool IsDetailEnabled(string processName, AppSettings settings) => processName.ToLowerInvariant() switch
    {
        "winword" => settings.EnableWordDetailPlugin,
        "excel" => settings.EnableExcelDetailPlugin,
        "code" or "code - insiders" => settings.EnableVsCodeDetailPlugin,
        "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" => settings.EnableBrowserDetailPlugin,
        _ => true
    };
}

public sealed class MicrosoftOfficeContextProvider : IActivityContextProvider
{
    /// <summary>
    /// Handles Word and Excel process naming conventions.
    /// </summary>
    public bool CanHandle(ForegroundWindowInfo window) =>
        window.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase) ||
        window.ProcessName.Equals("EXCEL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes title suffixes and emits document/workbook context attributes.
    /// </summary>
    public ActivityContext Resolve(ForegroundWindowInfo window)
    {
        var isWord = window.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase);
        var application = isWord ? "Microsoft Word" : "Microsoft Excel";
        var suffixes = isWord ? new[] { " - Word", " - Microsoft Word" } : new[] { " - Excel", " - Microsoft Excel" };
        var context = window.WindowTitle;

        foreach (var suffix in suffixes)
        {
            if (context.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                context = context[..^suffix.Length];
                break;
            }
        }

        return new ActivityContext(application, string.IsNullOrWhiteSpace(context) ? "Untitled document" : context,
            new Dictionary<string, string> { ["Type"] = isWord ? "Document" : "Workbook", ["Process"] = window.ProcessName });
    }
}

public sealed class AdobeCreativeContextProvider : IActivityContextProvider
{
    private static readonly Dictionary<string, string> AdobeApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Photoshop"] = "Adobe Photoshop",
        ["Adobe Photoshop"] = "Adobe Photoshop",
        ["PhotoshopBeta"] = "Adobe Photoshop",
        ["Illustrator"] = "Adobe Illustrator",
        ["InDesign"] = "Adobe InDesign",
        ["AfterFX"] = "Adobe After Effects",
        ["Premiere Pro"] = "Adobe Premiere Pro",
        ["PremierePro"] = "Adobe Premiere Pro",
        ["MediaEncoder"] = "Adobe Media Encoder",
        ["Adobe Premiere Pro (Beta)"] = "Adobe Premiere Pro",
        ["Audition"] = "Adobe Audition",
        ["Lightroom"] = "Adobe Lightroom",
        ["lightroom"] = "Adobe Lightroom",
        ["Adobe XD"] = "Adobe XD",
        ["XD"] = "Adobe XD",
        ["Acrobat"] = "Adobe Acrobat",
        ["AcrobatSDI"] = "Adobe Acrobat",
        ["AcroRd32"] = "Adobe Acrobat Reader",
    };

    private static readonly Dictionary<string, string> AdobeSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Photoshop"] = " - Photoshop",
        ["Adobe Photoshop"] = " - Adobe Photoshop",
        ["Illustrator"] = " - Illustrator",
        ["Adobe Illustrator"] = " - Adobe Illustrator",
        ["InDesign"] = " - InDesign",
        ["After Effects"] = " - After Effects",
        ["AfterFX"] = " - After Effects",
        ["Premiere Pro"] = " - Premiere Pro",
        ["Media Encoder"] = " - Media Encoder",
        ["Adobe Media Encoder"] = " - Adobe Media Encoder",
        ["Audition"] = " - Audition",
        ["Lightroom"] = " - Lightroom",
        ["Adobe XD"] = " - Adobe XD",
        ["XD"] = " - Adobe XD",
        ["Acrobat"] = " - Acrobat",
        ["Adobe Acrobat"] = " - Adobe Acrobat"
    };

    /// <summary>
    /// Handles Adobe Creative Suite and related apps for richer context details.
    /// </summary>
    public bool CanHandle(ForegroundWindowInfo window) => AdobeApplications.ContainsKey(window.ProcessName);

    /// <summary>
    /// Removes common title suffixes and adds studio/application metadata.
    /// </summary>
    public ActivityContext Resolve(ForegroundWindowInfo window)
    {
        var application = AdobeApplications.GetValueOrDefault(window.ProcessName, "Adobe Creative Suite");
        var context = window.WindowTitle;
        foreach (var suffix in AdobeSuffixes.Values)
        {
            if (context.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                context = context[..^suffix.Length];
                break;
            }
        }

        return new ActivityContext(application, string.IsNullOrWhiteSpace(context) ? "Untitled document / project" : context,
            new Dictionary<string, string> { ["Suite"] = "Adobe Creative Suite", ["Process"] = window.ProcessName });
    }
}

public sealed class KnownApplicationContextProvider : IActivityContextProvider
{
    /// <summary>
    /// Known process-name to human-readable application map.
    /// </summary>
    private static readonly Dictionary<string, string> Applications = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "Visual Studio Code",
        ["devenv"] = "Visual Studio",
        ["msedge"] = "Microsoft Edge",
        ["chrome"] = "Google Chrome",
        ["WindowsTerminal"] = "Windows Terminal",
        ["WindowsTerminalPreview"] = "Windows Terminal",
        ["ChatGPT"] = "ChatGPT",
        ["idea64"] = "IntelliJ IDEA",
        ["PyCharm64"] = "PyCharm",
        ["pycharm64"] = "PyCharm",
        ["pycharm"] = "PyCharm",
        ["Code - Insiders"] = "Visual Studio Code (Insiders)",
        ["notepad++"] = "Notepad++",
        ["CodeHelper"] = "CLion",
        ["clion64"] = "CLion",
        ["cl"] = "Microsoft C/C++ Compiler",
        ["cl.exe"] = "Microsoft C/C++ Compiler",
        ["gcc"] = "GCC",
        ["g++"] = "G++",
        ["g++-12"] = "G++",
        ["clang"] = "Clang",
        ["clang++"] = "Clang",
        ["clang-cl"] = "Clang/LLVM",
        ["cmake"] = "CMake",
        ["cc"] = "C Compiler",
        ["dotnet"] = "SDK .NET",
        ["msbuild"] = "MSBuild",
        ["make"] = "Make",
        ["nmake"] = "NMake",
        ["python"] = "Python",
        ["pythonw"] = "Python",
        ["java"] = "Java",
        ["javac"] = "Java Compiler",
        ["node"] = "Node.js",
        ["nodejs"] = "Node.js",
        ["electron"] = "Electron",
        ["Xcode"] = "Xcode",
        ["cargo"] = "Cargo",
        ["rustc"] = "Rust",
        ["go"] = "Go"
    };

    public bool CanHandle(ForegroundWindowInfo window) => Applications.ContainsKey(window.ProcessName);

    /// <summary>
    /// Resolves known process names to readable application labels.
    /// </summary>
    public ActivityContext Resolve(ForegroundWindowInfo window) =>
        new(Applications[window.ProcessName], string.IsNullOrWhiteSpace(window.WindowTitle) ? "No details" : window.WindowTitle,
            new Dictionary<string, string> { ["Process"] = window.ProcessName, ["Title"] = window.WindowTitle });
}

public sealed class GenericContextProvider : IActivityContextProvider
{
    /// <summary>
    /// Fallback provider for any process with minimal metadata.
    /// </summary>
    public bool CanHandle(ForegroundWindowInfo window) => true;

    /// <summary>
    /// Returns raw process/window values when no richer provider matches.
    /// </summary>
    public ActivityContext Resolve(ForegroundWindowInfo window) =>
        new(string.IsNullOrWhiteSpace(window.ProcessName) ? "System" : window.ProcessName,
            string.IsNullOrWhiteSpace(window.WindowTitle) ? "No details" : window.WindowTitle,
            new Dictionary<string, string> { ["Process"] = window.ProcessName, ["Title"] = window.WindowTitle });
}
