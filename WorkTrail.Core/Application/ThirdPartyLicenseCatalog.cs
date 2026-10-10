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


namespace WorkTrail.Application;

/// <summary>Describes one runtime dependency and the license terms shown in the About window.</summary>
public sealed record ThirdPartyLicense(string PackageName, string Version, string LicenseName);

/// <summary>Provides the direct runtime dependency inventory documented in THIRD_PARTY_NOTICES.md.</summary>
public static class ThirdPartyLicenseCatalog
{
    /// <summary>Gets the direct runtime packages shipped with WorkTrail.</summary>
    public static IReadOnlyList<ThirdPartyLicense> RuntimeDependencies { get; } =
    [
        new("Lucene.Net", "4.8.0-beta00018", "Apache-2.0"),
        new("Lucene.Net.Analysis.Common", "4.8.0-beta00018", "Apache-2.0"),
        new("Microsoft.Data.Sqlite", "10.0.10", "MIT"),
        new("Microsoft.Extensions.DependencyInjection", "10.0.10", "MIT"),
        new("Microsoft.Extensions.Logging", "10.0.10", "MIT"),
        new("Microsoft.Extensions.Logging.Abstractions", "10.0.10", "MIT"),
        new("Microsoft.Windows.SDK.BuildTools", "10.0.28000.2526", "Microsoft Windows SDK license terms"),
        new("Microsoft.WindowsAppSDK", "2.3.1", "Microsoft Software License Terms"),
        new("Sentry.Extensions.Logging", "6.9.0", "MIT"),
        new("Serilog", "4.4.0", "Apache-2.0"),
        new("Serilog.Extensions.Logging", "10.0.0", "Apache-2.0"),
        new("Serilog.Sinks.Console", "6.1.1", "Apache-2.0"),
        new("Serilog.Sinks.File", "7.0.0", "Apache-2.0"),
        new("SkiaSharp", "4.151.0", "MIT"),
        new("SkiaSharp.NativeAssets.Win32", "4.151.0", "MIT"),
        new("Spectre.Console", "0.57.2", "MIT"),
        new("SQLitePCLRaw.lib.e_sqlite3", "2.1.12", "Apache-2.0"),
        new("System.Diagnostics.PerformanceCounter", "10.0.10", "MIT"),
        new("System.Drawing.Common", "10.0.10", "MIT"),
        new("System.Management", "10.0.10", "MIT")
    ];
}
