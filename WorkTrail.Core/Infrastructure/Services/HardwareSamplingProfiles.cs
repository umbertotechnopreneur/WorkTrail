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


namespace WorkTrail.Services;

/// <summary>Defines the minimum polling interval for each supported hardware category.</summary>
public sealed record HardwareSamplingProfile(
    string Key,
    TimeSpan CpuInterval,
    TimeSpan MemoryInterval,
    TimeSpan BatteryInterval,
    TimeSpan StorageInterval,
    TimeSpan NetworkInterval)
{
    /// <summary>Gets the interval for a library hardware kind; GPU devices use the CPU interval.</summary>
    public TimeSpan GetInterval(string hardwareKind) => hardwareKind switch
    {
        "Cpu" or "GpuNvidia" or "GpuAmd" or "GpuIntel" => CpuInterval,
        "Memory" => MemoryInterval,
        "Battery" => BatteryInterval,
        "Storage" => StorageInterval,
        "Network" => NetworkInterval,
        _ => throw new ArgumentException("Unsupported hardware sampling category.", nameof(hardwareKind))
    };

    /// <summary>Checks whether a cached device reading is due under this profile without changing its original timestamp.</summary>
    public bool IsSampleDue(string hardwareKind, DateTimeOffset sampledAt, DateTimeOffset now) =>
        now - sampledAt >= GetInterval(hardwareKind);
}

/// <summary>Provides the canonical, ordered sampling profiles shared by settings, UI and collector.</summary>
public static class HardwareSamplingProfiles
{
    /// <summary>Gets all supported profiles, from lowest to highest polling frequency.</summary>
    public static IReadOnlyList<HardwareSamplingProfile> All { get; } = Array.AsReadOnly(new[]
    {
        Create("slow", 10, 30, 60, 120, 10),
        Create("normal", 2, 10, 30, 60, 2),
        Create("fast", 1, 5, 10, 30, 1),
        Create("fastest", 0.5, 2, 5, 30, 0.5)
    });

    /// <summary>Gets a canonical profile or rejects an unknown key without silently changing configuration.</summary>
    public static HardwareSamplingProfile Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return All.FirstOrDefault(profile => profile.Key == key)
            ?? throw new ArgumentException("Unknown hardware sampling profile.", nameof(key));
    }

    private static HardwareSamplingProfile Create(string key, double cpu, double memory, double battery, double storage, double network) =>
        new(key, TimeSpan.FromSeconds(cpu), TimeSpan.FromSeconds(memory), TimeSpan.FromSeconds(battery),
            TimeSpan.FromSeconds(storage), TimeSpan.FromSeconds(network));
}
