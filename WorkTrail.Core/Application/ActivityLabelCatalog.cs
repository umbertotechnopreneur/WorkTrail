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


using System.Text.Json;

namespace WorkTrail.Application;

/// <summary>A user-defined label. An empty ID is accepted only when requesting creation.</summary>
public sealed record ActivityLabelDefinition(string Id, string Name, string Icon, string Color);

/// <summary>Validates label commands and keeps the selected activity label consistent with its catalog.</summary>
public static class ActivityLabelCatalog
{
    private static readonly ActivityLabelDefinition[] DefaultDefinitions =
    [
        new("e184f328d8b74a43a67c70e28d366611", "Lavoro", "work", "#349CFA"),
        new("42c312fc6a214b4ea06c11a0d2643daf", "Studio", "study", "#AD7CF5"),
        new("aa8c658fcce942dda28de44a2573ec0c", "Personale", "home", "#3AC87B")
    ];

    /// <summary>Gets supported icon identifiers, independent of any presentation font.</summary>
    public static IReadOnlyList<string> Icons { get; } = Array.AsReadOnly(new[]
    {
        "folder", "work", "study", "home", "code", "book", "edit", "music",
        "art", "health", "sport", "travel", "globe", "tools", "heart", "gift"
    });

    /// <summary>Gets the available label colors.</summary>
    public static IReadOnlyList<string> Colors { get; } = Array.AsReadOnly(new[]
    {
        "#BEC6E2", "#FF6268", "#FF8B42", "#FFD642", "#3AC87B", "#349CFA", "#AD7CF5", "#F68CC8"
    });

    /// <summary>Creates the three starter labels used only for a fresh settings catalog.</summary>
    public static IReadOnlyList<ActivityLabelDefinition> CreateDefaultDefinitions() => DefaultDefinitions.ToArray();

    /// <summary>Rejects unsupported persisted definitions instead of silently discarding labels.</summary>
    public static void Validate(IReadOnlyList<ActivityLabelDefinition>? labels)
    {
        if (labels is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            if (label is null || !Guid.TryParseExact(label.Id, "N", out _)
                || !IsValidDefinition(label) || !ids.Add(label.Id) || !names.Add(label.Name))
            {
                throw new ArgumentException("Invalid or duplicate activity label.", nameof(labels));
            }
        }
    }

    /// <summary>Applies a create/update request; invalid JSON, IDs and duplicate names are rejected.</summary>
    public static bool TrySave(AppSettings settings, string? json, out AppSettings updated)
    {
        updated = settings;
        ActivityLabelDefinition? draft;
        try { draft = JsonSerializer.Deserialize<ActivityLabelDefinition>(json ?? "null"); }
        catch (JsonException) { return false; }
        if (draft is null || !IsValidDefinition(draft)) return false;
        var labels = (settings.ActivityLabels ?? []).ToList();
        var index = labels.FindIndex(label => label.Id == draft.Id);
        if (draft.Id != "" && index < 0) return false;
        if (labels.Any(label => label.Id != draft.Id && string.Equals(label.Name, draft.Name, StringComparison.OrdinalIgnoreCase))) return false;
        var selected = settings.SpanLabel;
        if (index >= 0)
        {
            if (selected == labels[index].Name) selected = draft.Name;
            labels[index] = draft;
        }
        else labels.Add(draft with { Id = Guid.NewGuid().ToString("N") });
        updated = settings with { ActivityLabels = labels.ToArray(), SpanLabel = selected };
        return true;
    }

    /// <summary>Deletes a known definition and clears its active selection; recorded activity is unchanged.</summary>
    public static bool TryDelete(AppSettings settings, string? id, out AppSettings updated)
    {
        updated = settings;
        var labels = settings.ActivityLabels ?? [];
        var target = labels.FirstOrDefault(label => label.Id == id);
        if (target is null) return false;
        updated = settings with
        {
            ActivityLabels = labels.Where(label => label.Id != id).ToArray(),
            SpanLabel = settings.SpanLabel == target.Name ? "" : settings.SpanLabel
        };
        return true;
    }

    private static bool IsValidDefinition(ActivityLabelDefinition label) =>
        label.Name is { Length: > 0 and <= 20 } && label.Name == label.Name.Trim()
        && !label.Name.Any(char.IsControl) && Icons.Contains(label.Icon) && Colors.Contains(label.Color);
}
