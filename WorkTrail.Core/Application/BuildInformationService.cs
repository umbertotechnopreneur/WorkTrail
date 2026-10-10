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
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Loads the immutable build provenance generated during compilation.</summary>
public sealed class BuildInformationService
{
    private const string FileName = "BuildInfo.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Loads and validates the distributed build manifest.</summary>
    public BuildInformation Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required build manifest is missing: {FileName}");
        }

        using var stream = File.OpenRead(path);
        var info = JsonSerializer.Deserialize<BuildInformation>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("The build manifest is empty or invalid.");

        if (info.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(info.SemVer) ||
            string.IsNullOrWhiteSpace(info.GitCommit) ||
            string.IsNullOrWhiteSpace(info.MachineName))
        {
            throw new InvalidOperationException("The build manifest is incomplete or unsupported.");
        }

        return info;
    }
}
