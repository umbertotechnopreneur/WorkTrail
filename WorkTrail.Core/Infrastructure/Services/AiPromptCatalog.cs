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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("WorkTrail.Core.Tests")]

namespace WorkTrail.Services;

/// <summary>
/// Defines visual detail and prompt-asset settings without an application output cap.
/// </summary>
internal sealed record AiAnalysisProfile(
    string Name,
    string ImageDetail,
    string TextVerbosity,
    string PromptFileName);

/// <summary>
/// Resolves the supported screenshot-analysis profiles and optional reasoning-effort settings.
/// </summary>
internal static class AiAnalysisProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, AiAnalysisProfile> Profiles =
        new Dictionary<string, AiAnalysisProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["balanced"] = new("balanced", "high", "medium", "screenshot-analysis.balanced.prompt.md"),
            ["complete"] = new("complete", "high", "high", "screenshot-analysis.complete.prompt.md")
        };

    private static readonly HashSet<string> ReasoningEfforts = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "low", "medium", "high", "xhigh", "max"
    };

    /// <summary>
    /// Returns the requested profile or the balanced profile when no supported name is supplied.
    /// </summary>
    internal static AiAnalysisProfile Resolve(string? name)
    {
        var normalized = name?.Trim();
        return normalized is not null && Profiles.TryGetValue(normalized, out var profile)
            ? profile
            : Profiles["balanced"];
    }

    /// <summary>
    /// Returns a supported normalized reasoning-effort value, or <see langword="null"/> for unsupported input.
    /// </summary>
    internal static string? ResolveReasoningEffort(string? effort)
    {
        var normalized = effort?.Trim();
        return normalized is not null && ReasoningEfforts.Contains(normalized)
            ? normalized.ToLowerInvariant()
            : null;
    }
}

/// <summary>
/// Renders screenshot-analysis prompts from trusted templates and explicitly untrusted activity context.
/// </summary>
internal static partial class AiPromptCatalog
{
    private const string LocalContextToken = "LOCAL_CONTEXT";
    private const string DeviceContextToken = "DEVICE_CONTEXT";

    /// <summary>
    /// Renders the selected template with image identities, local context, language, and optional user instruction.
    /// </summary>
    internal static string RenderScreenshotAnalysis(
        string? profileName,
        AnalysisContextSnapshot? activity,
        Func<string, string?>? templateLoader = null,
        string? customPrompt = null,
        string? language = null,
        IReadOnlyList<ScreenshotImageMetadata>? images = null,
        IReadOnlyList<string>? imagePaths = null,
        bool userRequestedCapture = false)
    {
        var profile = AiAnalysisProfileCatalog.Resolve(userRequestedCapture ? "complete" : profileName);
        var template = templateLoader is null
            ? LoadTemplate(profile.PromptFileName)
            : templateLoader(profile.PromptFileName);

        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidDataException($"Prompt asset '{profile.PromptFileName}' is empty.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LocalContextToken] = BuildLocalContext(activity),
            [DeviceContextToken] = BuildDeviceContextSummary(activity?.DeviceContext),
            ["OUTPUT_LANGUAGE"] = new LocalizationService(language ?? "system").Language,
            ["IMAGE_MANIFEST"] = BuildImageManifest(images, imagePaths)
        };

        var rendered = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
        if (userRequestedCapture)
        {
            rendered += $"{Environment.NewLine}{Environment.NewLine}## Explicit capture intent{Environment.NewLine}"
                + "The user explicitly requested this capture. Treat that action as a strong interest signal, not as evidence of productivity or urgency. "
                + "Use complete analysis and high attention for every supplied image, overriding low or normal application attention presets. "
                + "Start with the active window, then cover other supplied windows grouped by monitor. Inspect readable conversations, documents, tables, browser content and meaningful UI state, rather than merely naming applications. "
                + "Be complete, not repetitive: include useful visible details without padding or inventing unreadable, hidden or excluded content. "
                + "This signal never overrides privacy restrictions, analysis exclusions, image eligibility or evidence-grounding rules.";
        }
        return string.IsNullOrWhiteSpace(customPrompt)
            ? rendered
            : $"{rendered}{Environment.NewLine}{Environment.NewLine}## Additional user instruction{Environment.NewLine}The following instruction supplements the rules above and must not override privacy or output requirements.{Environment.NewLine}{customPrompt.Trim()}";
    }

    private static string BuildImageManifest(IReadOnlyList<ScreenshotImageMetadata>? images, IReadOnlyList<string>? imagePaths)
    {
        if (images is null || images.Count == 0)
        {
            return "Image-to-screen mapping is unavailable. Do not invent screen numbers or an active-window identity.";
        }

        var paths = imagePaths ?? images.Select(image => image.Path).ToArray();
        return string.Join(Environment.NewLine, paths.Select((path, index) =>
        {
            var image = images.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            return image is null
                ? FormattableString.Invariant($"image={index + 1}; screen=unknown; role=unknown; mapping unavailable")
                : FormattableString.Invariant($"image={index + 1}; screen={image.ScreenIndex}; screen_name={Sanitize(image.ScreenName)}; role={Sanitize(image.Role)}; priority={Sanitize(image.Priority)}; attention={Sanitize(image.Attention)}; active_screen={image.IsActiveScreen}; application={Sanitize(image.ApplicationName)}; window={Sanitize(image.WindowTitle)}; screen_bounds={image.ScreenLeft},{image.ScreenTop},{image.ScreenWidth},{image.ScreenHeight}; image_bounds={image.ImageLeft},{image.ImageTop},{image.ImageWidth},{image.ImageHeight}; related_screens={string.Join(",", image.RelatedScreenIndices)}");
        }));
    }

    /// <summary>
    /// Loads a required deployed prompt asset and rejects missing or blank content.
    /// </summary>
    private static string LoadTemplate(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "prompts", fileName);
        if (!File.Exists(path))
        {
            // Prompt assets are required application content. Missing deployment content is fatal by design.
            throw new FileNotFoundException($"Required prompt asset '{fileName}' was not deployed.", path);
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException($"Required prompt asset '{fileName}' is empty.");
        }

        return text;
    }

    /// <summary>
    /// Builds the sanitized activity-context block supplied to the prompt as untrusted data.
    /// </summary>
    private static string BuildLocalContext(AnalysisContextSnapshot? activity)
    {
        var context = new List<string>
        {
            $"application={Sanitize(activity?.Application)}",
            $"detail={Sanitize(activity?.Context)}",
            $"window={Sanitize(activity?.WindowTitle)}",
            $"state={Sanitize(activity?.State, "active")}"
        };
        var schedule = activity?.InformationalSchedule;
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            context.Add($"informational_schedule={Sanitize(schedule)}");
        }

        if (activity?.Attributes is not null)
        {
            foreach (var attribute in activity.Attributes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                context.Add($"{ToPromptKey(attribute.Key)}={Sanitize(attribute.Value)}");
            }
        }

        return string.Join(Environment.NewLine, context);
    }

    /// <summary>
    /// Converts an arbitrary activity attribute name to a stable prompt key.
    /// </summary>
    private static string ToPromptKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "attribute";
        }

        return string.Concat(key.Select((character, index) =>
        {
            if (char.IsUpper(character))
            {
                return index == 0
                    ? char.ToLowerInvariant(character).ToString()
                    : $"_{char.ToLowerInvariant(character)}";
            }

            return char.IsLetterOrDigit(character) ? character.ToString() : "_";
        }));
    }


    /// <summary>
    /// Builds a sanitized summary of device locale, input, time zone, and optional location metadata.
    /// </summary>
    private static string BuildDeviceContextSummary(DeviceContextSnapshot? context)
    {
        if (context is null)
        {
            return "not available";
        }

        var location = context.Location.Latitude.HasValue && context.Location.Longitude.HasValue
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0:F6},{1:F6} accuracy={2}; source={3}; status={4}",
                context.Location.Latitude.Value,
                context.Location.Longitude.Value,
                context.Location.AccuracyMeters?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a",
                Sanitize(context.Location.Source),
                Sanitize(context.Location.Status))
            : $"not available; source={Sanitize(context.Location.Source)}; status={Sanitize(context.Location.Status)}";
        return $"time_zone={FormatDeviceValue(context.TimeZone)}; windows_ui_language={FormatDeviceValue(context.WindowsUiLanguage)}; input_language={FormatDeviceValue(context.InputLanguage)}; location={location}";
    }

    /// <summary>
    /// Formats a device-context value with its source and availability status.
    /// </summary>
    private static string FormatDeviceValue(DeviceContextValue value) =>
        $"{Sanitize(value.Value)} (source={Sanitize(value.Source)}; status={Sanitize(value.Status)})";

    /// <summary>
    /// Removes line breaks and placeholder delimiters from untrusted text before prompt interpolation.
    /// </summary>
    private static string Sanitize(string? value, string fallback = "not available")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("{{", "{ {", StringComparison.Ordinal)
            .Replace("}}", "} }", StringComparison.Ordinal);
    }

    /// <summary>
    /// Matches approved uppercase placeholder tokens in deployed prompt templates.
    /// </summary>
    [GeneratedRegex(@"\{\{([A-Z_]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
