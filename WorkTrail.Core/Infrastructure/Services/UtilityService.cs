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
using System.IO;
using System.Reflection;

namespace WorkTrail.Services;

/// <summary>
/// Shared helper methods used across UI and services.
/// </summary>
public sealed class UtilityService
{
    private readonly Action<string, string> _apiKeyWriter;

    /// <summary>Creates the Windows utility service with the production user-and-process environment writer.</summary>
    public UtilityService()
        : this(SetUserAndProcessApiKey)
    {
    }

    internal UtilityService(Action<string, string> apiKeyWriter)
    {
        _apiKeyWriter = apiKeyWriter ?? throw new ArgumentNullException(nameof(apiKeyWriter));
    }

    /// <summary>
    /// Returns local application directory path under %LOCALAPPDATA%.
    /// </summary>
    public string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkTrail");

    /// <summary>
    /// Returns current application version in a compact string.
    /// </summary>
    public string GetAppVersion()
    {
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        return assemblyVersion is null ? "0.0.0" : assemblyVersion.ToString(3);
    }

    /// <summary>
    /// Stores API key in process and user environment.
    /// </summary>
    public void SetApiKey(string keyName, string value)
    {
        var name = string.IsNullOrWhiteSpace(keyName) ? throw new ArgumentException("A key variable is required.", nameof(keyName)) : keyName.Trim();
        _apiKeyWriter(name, value);
    }

    private static void SetUserAndProcessApiKey(string name, string value)
    {
        // Persist in both User and Process scopes: available now and on next app launches.
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    /// <summary>
    /// Returns a random, non-identifying installation identifier.
    /// </summary>
    public string GenerateInstallationId() => Guid.NewGuid().ToString("N");
}
