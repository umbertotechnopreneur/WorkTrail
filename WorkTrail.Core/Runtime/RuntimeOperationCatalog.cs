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

using System.Collections.ObjectModel;
using System.Reflection;

namespace WorkTrail.Runtime;

/// <summary>Identifies every operation supported by the versioned local runtime protocol.</summary>
internal enum RuntimeOperation
{
    [RuntimeOperationWireName("runtime.health")]
    RuntimeHealth,
    [RuntimeOperationWireName("world_clocks.get.v3")]
    WorldClocksGetV3,
    [RuntimeOperationWireName("celestial.reference.v1")]
    CelestialReferenceV1,
    [RuntimeOperationWireName("celestial.get.v1")]
    CelestialGetV1,
    [RuntimeOperationWireName("celestial.map.v1")]
    CelestialMapV1,
    [RuntimeOperationWireName("world_clocks.convert.v2")]
    WorldClocksConvertV2,
    [RuntimeOperationWireName("world_clocks.catalog.v1")]
    WorldClocksCatalogV1,
    [RuntimeOperationWireName("world_clocks.add.v3")]
    WorldClocksAddV3,
    [RuntimeOperationWireName("world_clocks.remove.v3")]
    WorldClocksRemoveV3,
    [RuntimeOperationWireName("world_clocks.move.v1")]
    WorldClocksMoveV1,
    [RuntimeOperationWireName("world_clocks.weather.key.set.v2")]
    WorldClocksWeatherKeySetV2,
    [RuntimeOperationWireName("diagnostics.log.open")]
    DiagnosticsLogOpen,
    [RuntimeOperationWireName("diagnostics.log.open_folder")]
    DiagnosticsLogOpenFolder,
    [RuntimeOperationWireName("diagnostics.log.share")]
    DiagnosticsLogShare,
    [RuntimeOperationWireName("settings.get")]
    SettingsGet,
    [RuntimeOperationWireName("features.access.get.v1")]
    FeatureAccessGetV1,
    [RuntimeOperationWireName("settings.patch")]
    SettingsPatch,
    [RuntimeOperationWireName("window.state.restore")]
    WindowStateRestore,
    [RuntimeOperationWireName("window.reveal_open.v1")]
    WindowRevealOpenV1,
    [RuntimeOperationWireName("window.state.save")]
    WindowStateSave,
    [RuntimeOperationWireName("window.open_state.set")]
    WindowOpenStateSet,
    [RuntimeOperationWireName("startup.status")]
    StartupStatus,
    [RuntimeOperationWireName("startup.enable")]
    StartupEnable,
    [RuntimeOperationWireName("startup.disable")]
    StartupDisable,
    [RuntimeOperationWireName("product.get")]
    ProductGet,
    [RuntimeOperationWireName("product.link.open")]
    ProductLinkOpen
}

/// <summary>Pairs a typed runtime operation with its stable wire name.</summary>
internal sealed record RuntimeOperationDefinition(RuntimeOperation Operation, string WireName);

/// <summary>Assigns the stable protocol name to a runtime operation.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class RuntimeOperationWireNameAttribute : Attribute
{

    #region Initialization
    /// <summary>Creates a stable wire-name annotation.</summary>
    internal RuntimeOperationWireNameAttribute(string wireName)
    {
        if (string.IsNullOrWhiteSpace(wireName)
            || wireName.Length > 128
            || wireName.Any(static character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not ('.' or '_')))
        {
            throw new ArgumentException("Runtime operation wire names must use lowercase ASCII protocol characters.", nameof(wireName));
        }

        WireName = wireName;
    }

    #endregion

    #region Methods and properties

    /// <summary>Gets the exact stable value serialized on the wire.</summary>
    internal string WireName { get; }

    #endregion

}

/// <summary>Owns the one-to-one mapping shared by the runtime host and client.</summary>
internal static class RuntimeOperationCatalog
{

    #region Fields
    private static readonly IReadOnlyList<RuntimeOperationDefinition> Definitions = CreateDefinitions();
    private static readonly IReadOnlyDictionary<string, RuntimeOperation> OperationsByWireName = BuildWireLookup(Definitions);
    private static readonly IReadOnlyDictionary<RuntimeOperation, string> WireNamesByOperation =
        new ReadOnlyDictionary<RuntimeOperation, string>(
            Definitions.ToDictionary(static definition => definition.Operation, static definition => definition.WireName));

    #endregion

    #region Methods and properties

    /// <summary>Gets every protocol operation exactly once.</summary>
    internal static IReadOnlyList<RuntimeOperationDefinition> All => Definitions;

    /// <summary>Resolves a typed operation from an untrusted request wire name.</summary>
    internal static bool TryResolve(string? wireName, out RuntimeOperation operation)
    {
        if (wireName is not null && OperationsByWireName.TryGetValue(wireName, out operation))
        {
            return true;
        }

        operation = default;
        return false;
    }

    /// <summary>Returns the exact wire name for a typed client operation.</summary>
    internal static string GetWireName(RuntimeOperation operation) =>
        WireNamesByOperation.TryGetValue(operation, out var wireName)
            ? wireName
            : throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown runtime operation.");

    /// <summary>Builds a lookup and rejects duplicate wire names immediately.</summary>
    internal static IReadOnlyDictionary<string, RuntimeOperation> BuildWireLookup(
        IEnumerable<RuntimeOperationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var lookup = new Dictionary<string, RuntimeOperation>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!lookup.TryAdd(definition.WireName, definition.Operation))
            {
                throw new InvalidOperationException($"Duplicate runtime operation wire name '{definition.WireName}'.");
            }
        }

        return new ReadOnlyDictionary<string, RuntimeOperation>(lookup);
    }

    #endregion

    #region Implementation

    /// <summary>Builds the operation lookup and rejects missing or duplicate wire names.</summary>
    private static IReadOnlyList<RuntimeOperationDefinition> CreateDefinitions()
    {
        var operations = Enum.GetValues<RuntimeOperation>();
        var definitions = operations.Select(operation =>
        {
            var member = typeof(RuntimeOperation).GetField(operation.ToString(), BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Runtime operation '{operation}' has no enum field.");
            var wireName = member.GetCustomAttribute<RuntimeOperationWireNameAttribute>()?.WireName
                ?? throw new InvalidOperationException($"Runtime operation '{operation}' has no wire name.");
            return new RuntimeOperationDefinition(operation, wireName);
        }).ToArray();
        if (definitions.Select(static definition => definition.Operation).Distinct().Count() != operations.Length)
        {
            throw new InvalidOperationException("The runtime operation catalog contains duplicate typed operations.");
        }

        _ = BuildWireLookup(definitions);
        return Array.AsReadOnly(definitions);
    }

    #endregion

}
