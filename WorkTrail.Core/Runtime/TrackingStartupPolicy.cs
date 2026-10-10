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


namespace WorkTrail.Runtime;

/// <summary>Resolves whether the shared tracking runtime should start for one application launch.</summary>
public static class TrackingStartupPolicy
{
    /// <summary>Combines explicit launch switches with the persisted start-on-launch preference.</summary>
    /// <param name="options">Parsed bootstrap options for the current process.</param>
    /// <param name="settings">Persisted application settings.</param>
    /// <returns><see langword="true"/> when tracking should be started or kept running.</returns>
    public static bool ShouldStart(LaunchOptions options, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        // Explicit pause and safe mode suppress both persisted and command-line start requests.
        return !options.Paused
            && !options.SafeMode
            && (options.StartTracking || settings.StartTrackingOnLaunch);
    }
}
