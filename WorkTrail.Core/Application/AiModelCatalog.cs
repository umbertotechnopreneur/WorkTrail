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
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WorkTrail.Application;

/// <summary>Describes one model that may be offered by an AI configuration surface.</summary>
public sealed record AiModelDescriptor(
    string Key,
    IReadOnlyList<string> Aliases,
    string Name,
    string Description,
    string Color,
    IReadOnlyList<string> SupportedThinkingEfforts,
    bool SupportsImageInput,
    string Availability,
    bool IsPreview);

/// <summary>Provides the serializable, presentation-neutral model catalog snapshot.</summary>
public sealed record AiModelCatalogSnapshot(
    int SchemaVersion,
    IReadOnlyList<AiModelDescriptor> Models);

/// <summary>Loads and validates the deployed AI model catalog.</summary>
public sealed partial class AiModelCatalog
{
    /// <summary>The deployed configuration file name.</summary>
    public const string DefaultFileName = "appsettings.json";

    private const int CurrentSchemaVersion = 1;
    private static readonly HashSet<string> AllowedThinkingEfforts = new(StringComparer.Ordinal)
    {
        "auto", "none", "low", "medium", "high", "xhigh", "max"
    };
    private static readonly HashSet<string> AllowedAvailability = new(StringComparer.Ordinal)
    {
        "general", "limited", "research-preview"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IReadOnlyDictionary<string, AiModelDescriptor> _modelsByIdentifier;

    private AiModelCatalog(
        int schemaVersion,
        IReadOnlyList<AiModelDescriptor> models,
        IReadOnlyDictionary<string, AiModelDescriptor> modelsByIdentifier)
    {
        SchemaVersion = schemaVersion;
        Models = models;
        Snapshot = new AiModelCatalogSnapshot(schemaVersion, models);
        _modelsByIdentifier = modelsByIdentifier;
    }

    /// <summary>Gets the validated catalog schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets models in their configured display order.</summary>
    public IReadOnlyList<AiModelDescriptor> Models { get; }

    /// <summary>Gets the immutable DTO returned through the application facade.</summary>
    public AiModelCatalogSnapshot Snapshot { get; }

    /// <summary>Loads the catalog deployed next to the running application.</summary>
    public static AiModelCatalog LoadDefault() =>
        Load(Path.Combine(AppContext.BaseDirectory, DefaultFileName));

    /// <summary>Loads a catalog from an explicit file path.</summary>
    public static AiModelCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    /// <summary>Reads a catalog from a caller-owned stream without closing it.</summary>
    public static AiModelCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The model catalog stream must be readable.", nameof(stream));
        }

        var document = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("The model catalog file must contain a JSON object.");
        return Validate(document);
    }

    /// <summary>Resolves a canonical model key or one of its aliases.</summary>
    public bool TryResolve(string identifier, out AiModelDescriptor? model)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            model = null;
            return false;
        }

        return _modelsByIdentifier.TryGetValue(identifier.Trim(), out model);
    }

    private static AiModelCatalog Validate(CatalogFile document)
    {
        var payload = document.AiModelCatalog
            ?? throw Invalid("The 'aiModelCatalog' object is required.");
        if (payload.SchemaVersion != CurrentSchemaVersion)
        {
            throw Invalid($"Unsupported AI model catalog schema version '{payload.SchemaVersion}'.");
        }

        var configuredModels = payload.Models
            ?? throw Invalid("The AI model catalog requires a 'models' array.");
        if (configuredModels.Count == 0)
        {
            throw Invalid("The AI model catalog must contain at least one model.");
        }

        var canonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in configuredModels)
        {
            if (configured is null)
            {
                throw Invalid("The AI model catalog cannot contain null model entries.");
            }

            var modelKey = RequireIdentifier(configured.Key, "model key");
            if (!canonicalKeys.Add(modelKey))
            {
                throw Invalid($"Duplicate model key '{modelKey}'.");
            }
        }

        var descriptors = new List<AiModelDescriptor>(configuredModels.Count);
        var lookup = new Dictionary<string, AiModelDescriptor>(StringComparer.OrdinalIgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in configuredModels)
        {
            if (configured is null)
            {
                throw Invalid("The AI model catalog cannot contain null model entries.");
            }

            var modelKey = RequireIdentifier(configured.Key, "model key");
            var name = RequireText(configured.Name, "name", 100);
            var description = RequireText(configured.Description, "description", 500);
            var color = configured.Color;
            if (color is null || !ColorRegex().IsMatch(color))
            {
                throw Invalid($"Model '{modelKey}' has invalid color '{color}'. Expected #RRGGBB.");
            }

            var availability = configured.Availability;
            if (availability is null || !AllowedAvailability.Contains(availability))
            {
                throw Invalid($"Model '{modelKey}' has invalid availability '{availability}'.");
            }

            if (!configured.IsPreview.HasValue)
            {
                throw Invalid($"Model '{modelKey}' requires an 'isPreview' value.");
            }

            if (!configured.SupportsImageInput.HasValue)
            {
                throw Invalid($"Model '{modelKey}' requires a 'supportsImageInput' value.");
            }

            if (configured.IsPreview.Value != availability.Equals("research-preview", StringComparison.Ordinal))
            {
                throw Invalid($"Model '{modelKey}' has inconsistent preview and availability values.");
            }

            var modelAliases = ValidateAliases(modelKey, configured.Aliases, canonicalKeys, aliases);
            var thinkingEfforts = ValidateEfforts(
                modelKey,
                "thinking",
                configured.SupportedThinkingEfforts,
                AllowedThinkingEfforts);
            if (thinkingEfforts.Count == 0)
            {
                throw Invalid($"Model '{modelKey}' must define at least one supported thinking effort.");
            }

            var descriptor = new AiModelDescriptor(
                modelKey,
                modelAliases,
                name,
                description,
                color,
                thinkingEfforts,
                configured.SupportsImageInput.Value,
                availability,
                configured.IsPreview.Value);
            descriptors.Add(descriptor);
            lookup.Add(descriptor.Key, descriptor);
            foreach (var alias in descriptor.Aliases)
            {
                lookup.Add(alias, descriptor);
            }
        }

        return new AiModelCatalog(
            payload.SchemaVersion,
            descriptors.AsReadOnly(),
            new Dictionary<string, AiModelDescriptor>(lookup, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ValidateAliases(
        string modelKey,
        List<string>? configuredAliases,
        IReadOnlySet<string> canonicalKeys,
        ISet<string> aliases)
    {
        if (configuredAliases is null)
        {
            throw Invalid($"Model '{modelKey}' requires an 'aliases' array.");
        }

        var result = new List<string>(configuredAliases.Count);
        foreach (var alias in configuredAliases)
        {
            var validatedAlias = RequireIdentifier(alias, $"alias for model '{modelKey}'");
            if (canonicalKeys.Contains(validatedAlias) || !aliases.Add(validatedAlias))
            {
                throw Invalid($"Duplicate model identifier '{validatedAlias}'.");
            }

            result.Add(validatedAlias);
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<string> ValidateEfforts(
        string modelKey,
        string effortKind,
        List<string>? configuredEfforts,
        IReadOnlySet<string> allowedEfforts)
    {
        if (configuredEfforts is null)
        {
            throw Invalid($"Model '{modelKey}' requires a 'supported{char.ToUpperInvariant(effortKind[0])}{effortKind[1..]}Efforts' array.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effort in configuredEfforts)
        {
            if (effort is null || !allowedEfforts.Contains(effort))
            {
                throw Invalid($"Model '{modelKey}' has invalid {effortKind} effort '{effort}'.");
            }

            if (!seen.Add(effort))
            {
                throw Invalid($"Model '{modelKey}' repeats {effortKind} effort '{effort}'.");
            }
        }

        return configuredEfforts.AsReadOnly();
    }

    private static string RequireIdentifier(string? value, string field)
    {
        if (value is null || !ModelIdentifierRegex().IsMatch(value))
        {
            throw Invalid($"Invalid {field} '{value}'.");
        }

        return value;
    }

    private static string RequireText(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !value.Equals(value.Trim(), StringComparison.Ordinal)
            || value.IndexOf('\0') >= 0)
        {
            throw Invalid($"The model {field} is invalid.");
        }

        return value;
    }

    private static InvalidDataException Invalid(string message) => new(message);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelIdentifierRegex();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();

    private sealed class CatalogFile
    {
        [JsonPropertyName("aiModelCatalog")]
        public CatalogPayload? AiModelCatalog { get; init; }
    }

    private sealed class CatalogPayload
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("models")]
        public List<ModelConfiguration?>? Models { get; init; }
    }

    private sealed class ModelConfiguration
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("aliases")]
        public List<string>? Aliases { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("color")]
        public string? Color { get; init; }

        [JsonPropertyName("supportedThinkingEfforts")]
        public List<string>? SupportedThinkingEfforts { get; init; }

        [JsonPropertyName("supportsImageInput")]
        public bool? SupportsImageInput { get; init; }

        [JsonPropertyName("availability")]
        public string? Availability { get; init; }

        [JsonPropertyName("isPreview")]
        public bool? IsPreview { get; init; }
    }
}
