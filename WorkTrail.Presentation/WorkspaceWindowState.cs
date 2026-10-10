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


using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Selects durable work surfaces while leaving temporary dialogs and operations closed.</summary>
public static class WorkspaceWindowState
{
    private static readonly string[] RestoreOrder =
    [
        WindowStateKeys.Main,
        WindowStateKeys.WorldClocks,
        WindowStateKeys.WorldMap,
        WindowStateKeys.LunarPhase,
        WindowStateKeys.LocalSky,
        WindowStateKeys.AstronomyAgenda,
        WindowStateKeys.CelestialMap,
        WindowStateKeys.Search,
        WindowStateKeys.Screenshots,
        WindowStateKeys.OcrText,
        WindowStateKeys.Schedule,
        WindowStateKeys.About,
        WindowStateKeys.Licenses
    ];

    /// <summary>Identifies a window that can reopen without replaying a configuration or background operation.</summary>
    public static bool IsRestorable(string windowKey)
    {
        _ = WindowStateService.GetMinimumSize(windowKey);
        return RestoreOrder.Contains(windowKey, StringComparer.Ordinal);
    }

    /// <summary>Treats live auxiliary windows as open even when Windows temporarily hides an owned surface.</summary>
    public static bool IsOpenWhileAlive(string windowKey, bool isVisible)
    {
        if (!IsRestorable(windowKey))
        {
            throw new ArgumentException("Transient windows do not participate in workspace visibility.", nameof(windowKey));
        }

        // Only the main window has a user-facing hide-to-notification-area action.
        return windowKey != WindowStateKeys.Main || isVisible;
    }

    /// <summary>Restores saved open surfaces in owner-first order, including owners needed by retained child windows.</summary>
    public static IReadOnlyList<string> GetWindowsToRestore(IReadOnlyDictionary<string, bool>? openStates)
    {
        if (openStates is null)
        {
            return [];
        }

        foreach (var key in openStates.Keys)
        {
            // Known transient dialogs remain closed; unsupported identities must not disappear from a saved session.
            _ = WindowStateService.GetMinimumSize(key);
        }

        var requested = openStates.Where(static pair => pair.Value).Select(static pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (requested.Contains(WindowStateKeys.OcrText))
        {
            requested.Add(WindowStateKeys.Screenshots);
        }

        if (requested.Contains(WindowStateKeys.Licenses))
        {
            requested.Add(WindowStateKeys.About);
        }

        return RestoreOrder.Where(requested.Contains).ToArray();
    }
}
