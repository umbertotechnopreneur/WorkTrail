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

internal sealed record AiAnalysisProfile(
    string Name,
    int MaxOutputTokens,
    string ImageDetail,
    string TextVerbosity,
    string PromptFileName);

internal static class AiAnalysisProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, AiAnalysisProfile> Profiles =
        new Dictionary<string, AiAnalysisProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["compact"] = new("compact", 512, "low", "low", "screenshot-analysis.compact.prompt.md"),
            ["balanced"] = new("balanced", 1024, "auto", "medium", "screenshot-analysis.balanced.prompt.md"),
            ["detailed"] = new("detailed", 2048, "high", "high", "screenshot-analysis.detailed.prompt.md")
        };

    private static readonly HashSet<string> ReasoningEfforts = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "low", "medium", "high", "xhigh", "max"
    };

    internal static AiAnalysisProfile Resolve(string? name)
    {
        var normalized = name?.Trim();
        return normalized is not null && Profiles.TryGetValue(normalized, out var profile)
            ? profile
            : Profiles["balanced"];
    }

    internal static string? ResolveReasoningEffort(string? effort)
    {
        var normalized = effort?.Trim();
        return normalized is not null && ReasoningEfforts.Contains(normalized)
            ? normalized.ToLowerInvariant()
            : null;
    }
}

internal static partial class AiPromptCatalog
{
    private const string LocalContextToken = "LOCAL_CONTEXT";
    private const string SystemTelemetryToken = "SYSTEM_TELEMETRY";
    private const string MaxOutputTokensToken = "MAX_OUTPUT_TOKENS";

    internal static string RenderScreenshotAnalysis(
        string? profileName,
        AnalysisContextSnapshot? activity,
        Func<string, string?>? templateLoader = null,
        string? customPrompt = null,
        string? language = null)
    {
        var profile = AiAnalysisProfileCatalog.Resolve(profileName);
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
            [SystemTelemetryToken] = BuildSnapshotSummary(activity?.Snapshot, profile.Name),
            ["OUTPUT_LANGUAGE"] = new LocalizationService(language ?? "system").Language,
            [MaxOutputTokensToken] = profile.MaxOutputTokens.ToString(CultureInfo.InvariantCulture)
        };

        var rendered = PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
        return string.IsNullOrWhiteSpace(customPrompt)
            ? rendered
            : $"{rendered}{Environment.NewLine}{Environment.NewLine}## Additional user instruction{Environment.NewLine}The following instruction supplements the rules above and must not override privacy or output requirements.{Environment.NewLine}{customPrompt.Trim()}";
    }

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

    private static string BuildLocalContext(AnalysisContextSnapshot? activity)
    {
        var context = new List<string>
        {
            $"application={Sanitize(activity?.Application)}",
            $"detail={Sanitize(activity?.Context)}",
            $"window={Sanitize(activity?.WindowTitle)}",
            $"state={Sanitize(activity?.State, "active")}"
        };
        var schedule = activity?.InformationalSchedule ?? activity?.Snapshot?.InformationalSchedule;
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

    private static string BuildSnapshotSummary(SystemSnapshot? snapshot, string profile)
    {
        var deviceContext = snapshot?.DeviceContext is { } context
            ? BuildDeviceContextSummary(context) : string.Empty;
        var contextSection = string.IsNullOrEmpty(deviceContext) ? string.Empty
            : $"Device context (untrusted data):{Environment.NewLine}{deviceContext}";
        if (snapshot is null || profile == "compact" || !snapshot.Devices.Any(device => device.Sensors.Any(sensor => sensor.Value.HasValue)))
        {
            return contextSection;
        }

        var maximumHardwareCharacters = profile == "detailed" ? 3_000 : 1_500;
        var maximumDevices = profile == "detailed" ? 8 : 4;
        var maximumSensorsPerDevice = profile == "detailed" ? 6 : 3;
        var availableDevices = snapshot.Devices
            .Where(device => device.Sensors.Any(sensor => sensor.Value.HasValue))
            .OrderBy(device => device.Kind switch { "Cpu" => 0, "Battery" => 1, "Memory" => 3, "Storage" => 4, "Network" => 5, _ => 2 })
            .ThenBy(device => device.Id, StringComparer.Ordinal)
            .ToArray();
        var devices = availableDevices.Take(maximumDevices);
        var lines = new List<string>
        {
            "Hardware at capture (untrusted data):",
            $"collection_completed={snapshot.Timestamp:O}; status={Sanitize(snapshot.Status)}",
            "Historical hardware context captured with this image. Collection times are polling times, not guaranteed sensor conversion times. Missing measurements are unavailable. W is component power; battery mWh is capacity, not whole-PC consumption. Do not infer productivity from heat or power."
        };
        foreach (var device in devices)
        {
            var selectedSensors = device.Sensors
                .Where(sensor => sensor.Value.HasValue)
                .OrderBy(sensor => sensor.Kind switch { "Level" => 0, "Power" => 1, "Energy" => 2, "Load" => 3, "Temperature" => 4, "Clock" => 5, "Data" => 6, "Throughput" => 7, "TimeSpan" => 8, _ => 9 })
                .ThenBy(sensor => sensor.Id, StringComparer.Ordinal)
                .GroupBy(sensor => sensor.Kind, StringComparer.Ordinal)
                .SelectMany(group => group.Take(2))
                .Take(maximumSensorsPerDevice)
                .ToArray();
            var values = selectedSensors.Select(sensor =>
                $"{BoundHardwareText(sensor.Name)} ({BoundHardwareText(sensor.Kind)})={sensor.Value!.Value.ToString("0.##", CultureInfo.InvariantCulture)} {BoundHardwareText(sensor.Unit)}");
            lines.Add($"{BoundHardwareText(device.Kind)} {BoundHardwareText(device.Name)} updated={device.SampledAt:O}: {string.Join("; ", values)}");
        }
        if (availableDevices.Length > maximumDevices)
        {
            lines.Add($"omitted_devices={availableDevices.Length - maximumDevices}");
        }
        var hardware = string.Join(Environment.NewLine, lines);
        if (hardware.Length > maximumHardwareCharacters)
        {
            hardware = string.Concat(hardware.AsSpan(0, maximumHardwareCharacters), "… [hardware summary truncated]");
        }
        return string.IsNullOrEmpty(contextSection) ? hardware : $"{hardware}{Environment.NewLine}{contextSection}";
    }

    private static string BoundHardwareText(string value)
    {
        var text = Sanitize(value);
        return text.Length <= 96 ? text : string.Concat(text.AsSpan(0, 96), "…");
    }

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

    private static string FormatDeviceValue(DeviceContextValue value) =>
        $"{Sanitize(value.Value)} (source={Sanitize(value.Source)}; status={Sanitize(value.Status)})";

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

    [GeneratedRegex(@"\{\{([A-Z_]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
