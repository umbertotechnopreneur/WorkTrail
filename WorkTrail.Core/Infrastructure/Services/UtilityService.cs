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
    /// Returns default screenshot storage directory.
    /// </summary>
    public string GetDefaultScreenshotDirectory() => Path.Combine(AppDataDirectory, "screenshots");

    /// <summary>
    /// Returns current application version in a compact string.
    /// </summary>
    public string GetAppVersion()
    {
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        return assemblyVersion is null ? "0.0.0" : assemblyVersion.ToString(3);
    }

    /// <summary>
    /// Expands environment variables, creates directory and returns the canonical path.
    /// </summary>
    /// <param name="value">Input directory, or empty for default.</param>
    public string NormalizeDirectory(string? value)
    {
        var directory = string.IsNullOrWhiteSpace(value) ? GetDefaultScreenshotDirectory() : Environment.ExpandEnvironmentVariables(value.Trim());
        Directory.CreateDirectory(directory);
        return Path.GetFullPath(directory);
    }

    /// <summary>
    /// Stores API key in process and user environment.
    /// </summary>
    public void SetApiKey(string keyName, string value)
    {
        var name = string.IsNullOrWhiteSpace(keyName) ? "OPENAI_API_KEY" : keyName.Trim();
        _apiKeyWriter(name, value);
    }

    private static void SetUserAndProcessApiKey(string name, string value)
    {
        // Persist in both User and Process scopes: available now and on next app launches.
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    /// <summary>
    /// Formats seconds as a compact duration (e.g., "2 h 35 min").
    /// </summary>
    public string FormatDuration(long seconds) => seconds >= 3600 ? $"{seconds / 3600} h {(seconds % 3600) / 60} min" : $"{Math.Max(0, seconds / 60)} min";

    /// <summary>
    /// Returns a random, non-identifying installation identifier.
    /// </summary>
    public string GenerateInstallationId() => Guid.NewGuid().ToString("N");

    /// <summary>Returns the current Windows machine name used as immutable installation provenance.</summary>
    public string GetMachineName()
    {
        var machineName = Environment.MachineName.Trim();
        if (machineName.Length is < 1 or > 128)
        {
            throw new InvalidOperationException("Windows returned an invalid machine name.");
        }

        return machineName;
    }
}
