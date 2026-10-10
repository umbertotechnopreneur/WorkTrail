// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkTrail.Services;

/// <summary>
/// Resolves supported UI locales and serves validated localized product copy.
/// </summary>
public sealed class LocalizationService
{
    private const string ResourcePrefix = "WorkTrail.Localization.";

    private static readonly IReadOnlyDictionary<string, string> CultureNames =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProductLanguageCatalog.EnglishLanguage] = "en-US",
            ["it-IT"] = "it-IT",
            ["fr-FR"] = "fr-FR",
            ["de-DE"] = "de-DE",
            ["es-ES"] = "es-ES",
            ["vi-VN"] = "vi-VN",
            ["zh-Hans"] = "zh-CN",
            ["ko-KR"] = "ko-KR",
            ["pt-PT"] = "pt-PT",
            ["pt-BR"] = "pt-BR"
        });

    private static readonly Regex FormatItemPattern = new(
        @"\{[0-9]+(?:,-?[0-9]+)?(?::[^{}]+)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = LoadCatalogs();
    private static readonly IReadOnlyDictionary<string, string> AutomationNames = LoadAutomationNames();

    /// <summary>Gets stable UI automation identifiers and their localized name keys.</summary>
    public static IReadOnlyDictionary<string, string> AutomationNameKeys => AutomationNames;

    /// <summary>Gets the canonical explicit language codes accepted by the application.</summary>
    public static IReadOnlyList<string> SupportedLanguages => ProductLanguageCatalog.UiLocales;

    /// <summary>Gets the resolved canonical application language code.</summary>
    public string Language { get; }

    /// <summary>Gets the persisted language choice before system-language resolution.</summary>
    public string RequestedLanguage { get; }

    /// <summary>Gets the specific culture used for dates, numbers, and localized formatting.</summary>
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureNames[Language]);

    /// <summary>Creates a localization service for an explicit locale or the Windows UI locale.</summary>
    public LocalizationService(string language)
    {
        var requested = string.IsNullOrWhiteSpace(language) ? ProductLanguageCatalog.SystemLanguage : language.Trim();
        Language = ResolveLanguage(requested, CultureInfo.CurrentUICulture);
        RequestedLanguage = requested.Equals(ProductLanguageCatalog.SystemLanguage, StringComparison.OrdinalIgnoreCase)
            ? ProductLanguageCatalog.SystemLanguage
            : Language;
    }

    /// <summary>Resolves an explicit locale before consulting Windows for the system choice.</summary>
    public static string ResolveLanguage(string? requestedLanguage, CultureInfo systemCulture)
        => ProductLanguageCatalog.ResolveUiLanguage(requestedLanguage, systemCulture);

    /// <summary>Returns localized text for a required catalog key.</summary>
    public string Translate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Catalogs[Language].TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Localization key '{key}' is not defined for '{Language}'.");
    }

    /// <summary>Formats localized text with the resolved locale's number and date conventions.</summary>
    public string Format(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(Culture, Translate(key), arguments);
    }

    /// <summary>Tries to resolve localized text for a catalog key.</summary>
    public bool TryTranslate(string key, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Catalogs[Language].TryGetValue(key, out value!);
    }

    /// <summary>Resolves the accessible name of a control registered in the UI automation catalog.</summary>
    /// <param name="automationId">The stable, untranslated control identifier.</param>
    /// <param name="value">The name in the currently selected application language.</param>
    public bool TryTranslateAutomationName(string automationId, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        if (AutomationNames.TryGetValue(automationId, out var key))
        {
            value = Translate(key);
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Loads the dedicated control-name map and checks its keys against every supported locale.</summary>
    private static IReadOnlyDictionary<string, string> LoadAutomationNames()
    {
        const string resourceName = "WorkTrail.Data.ui-automation-names.json";
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Required automation-name resource '{resourceName}' is missing.");
        var names = LoadCatalog(stream, resourceName);
        foreach (var (automationId, key) in names)
        {
            foreach (var (language, catalog) in Catalogs)
            {
                if (!catalog.TryGetValue(key, out var name) || FormatItems(name).Length != 0)
                {
                    throw new InvalidDataException(
                        $"Automation name '{automationId}' requires a non-formatted localization key '{key}' in '{language}'.");
                }
            }
        }

        return names;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(LocalizationService).Assembly;
        foreach (var language in ProductLanguageCatalog.UiLocales)
        {
            var resourceName = $"{ResourcePrefix}{language}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Required localization resource '{resourceName}' is missing.");
            catalogs.Add(language, LoadCatalog(stream, resourceName));
        }

        ValidateCatalogParity(catalogs);
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(catalogs);
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(Stream stream, string resourceName)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Localization resource '{resourceName}' must contain a JSON object.");
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"Localization resource '{resourceName}' contains an invalid entry.");
                }

                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || !entries.TryAdd(property.Name, value))
                {
                    throw new InvalidDataException($"Localization resource '{resourceName}' contains an empty or duplicate entry for '{property.Name}'.");
                }

                try
                {
                    _ = CompositeFormat.Parse(value);
                }
                catch (FormatException exception)
                {
                    throw new InvalidDataException(
                        $"Localization resource '{resourceName}' contains an invalid composite format for '{property.Name}'.",
                        exception);
                }
            }

            return new ReadOnlyDictionary<string, string>(entries);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Localization resource '{resourceName}' is not valid JSON.", exception);
        }
    }

    private static void ValidateCatalogParity(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalogs)
    {
        var english = catalogs[ProductLanguageCatalog.EnglishLanguage];
        foreach (var (language, catalog) in catalogs)
        {
            var missing = english.Keys.Except(catalog.Keys, StringComparer.Ordinal).OrderBy(static key => key).ToArray();
            var unexpected = catalog.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(static key => key).ToArray();
            if (missing.Length > 0 || unexpected.Length > 0)
            {
                throw new InvalidDataException(
                    $"Localization resource '{language}' does not match English. Missing: [{string.Join(", ", missing)}]. Unexpected: [{string.Join(", ", unexpected)}].");
            }

            foreach (var key in english.Keys)
            {
                var expectedItems = FormatItems(english[key]);
                var localizedItems = FormatItems(catalog[key]);
                if (!expectedItems.SequenceEqual(localizedItems, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"Localization resource '{language}' changes format items for '{key}'.");
                }
            }
        }
    }

    private static string[] FormatItems(string value) =>
        FormatItemPattern.Matches(value)
            .Select(static match => match.Value)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
}
