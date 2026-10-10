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

namespace WorkTrail.Runtime;

/// <summary>Preserves tracking, runtime mode and safety choices when memory recovery relaunches the process.</summary>
public static class MemoryRecoveryLaunchPolicy
{
    /// <summary>Creates fixed bootstrap arguments without forwarding arbitrary command-line or provider content.</summary>
    /// <param name="options">The most recent effective runtime launch options.</param>
    /// <param name="isTracking">Whether tracking was active before recovery paused the runtime.</param>
    public static string CreateArguments(LaunchOptions options, bool isTracking)
    {
        ArgumentNullException.ThrowIfNull(options);
        var arguments = new List<string>
        {
            options.Mode == LaunchMode.Background ? "--background" : "--ui",
            "--no-splash",
            "--memory-recovery",
            isTracking && !options.SafeMode ? "--start-tracking" : "--paused"
        };
        if (options.SafeMode) arguments.Add("--safe-mode");
        if (options.StartWithWindows) arguments.Add("--start-with-windows");
        if (options.Language is not null)
        {
            // Only catalog choices become command-line values; no raw settings or other launch arguments are forwarded.
            var language = ProductLanguageCatalog.CanonicalUiChoice(options.Language)
                ?? throw new ArgumentException("The recovery language is unsupported.", nameof(options));
            arguments.Add("--language");
            arguments.Add(language);
        }
        return string.Join(' ', arguments);
    }
}
