// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkTrail.Application;

/// <summary>One source-backed national holiday or make-up workday in the bundled agenda catalog.</summary>
internal sealed record CelestialCalendarHoliday(
    DateOnly Date, string Country, string Name, string Kind, string Quality,
    string ArtworkFileName, string SourceUrl);

/// <summary>One fixed-date Latin sanctoral entry with a stable artwork identifier.</summary>
internal sealed record CelestialCalendarSaint(
    int Month, int Day, string EventKey, string NameLatin, string SourceUrl);

/// <summary>Normalized source catalog imported into the application's existing SQLite store.</summary>
internal sealed record CelestialCalendarDataset(
    string Sha256, DateOnly CoverageStart, DateOnly CoverageEnd,
    string HolidayLibrary, string SaintsRevision,
    IReadOnlyList<CelestialCalendarCountrySource> Countries,
    IReadOnlyList<CelestialCalendarHoliday> Holidays,
    IReadOnlyList<CelestialCalendarSaint> Saints);

/// <summary>Source metadata for one national holiday calendar.</summary>
internal sealed record CelestialCalendarCountrySource(string Code, string Name, string Scope, string SourceUrl);

/// <summary>Validates the fixed JSON asset before any catalog rows are persisted or displayed.</summary>
internal static class CelestialCalendarCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private static readonly Lazy<CelestialCalendarDataset> Loaded = new(Load);

    /// <summary>Gets the reviewed catalog embedded in WorkTrail.Core.</summary>
    internal static CelestialCalendarDataset Current => Loaded.Value;

    private static CelestialCalendarDataset Load()
    {
        using var stream = typeof(CelestialCalendarCatalog).Assembly.GetManifestResourceStream(
            "WorkTrail.Data.celestial-calendar.json")
            ?? throw new InvalidDataException("The embedded celestial calendar catalog is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var raw = JsonSerializer.Deserialize<RawDataset>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The celestial calendar catalog is empty.");
        if (raw.SchemaVersion != 2 || raw.HolidayLibrary != "holidays 0.105"
            || raw.SaintsSource.Revision != "1bb2b7c503a701a9713b2f881795afe46044af3b"
            || raw.Countries.Count != CelestialCalendarCountries.All.Count || raw.Saints.Count != 211
            || raw.Holidays.Count == 0)
        {
            throw new InvalidDataException("The celestial calendar catalog version or coverage is unsupported.");
        }

        var start = ParseDate(raw.CoverageStart);
        var end = ParseDate(raw.CoverageEnd);
        if (start != new DateOnly(2026, 1, 1) || end != new DateOnly(2028, 3, 31))
        {
            throw new InvalidDataException("The celestial calendar date range is unsupported.");
        }

        var countries = raw.Countries.Select(item =>
        {
            if (!CelestialCalendarCountries.All.Any(country => country.Code == item.Code)
                || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Scope)
                || !IsHttps(item.SourceUrl))
                throw new InvalidDataException($"Invalid celestial calendar country: {item.Code}");
            return new CelestialCalendarCountrySource(item.Code, item.Name, item.Scope, item.SourceUrl);
        }).ToArray();
        if (countries.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != countries.Length)
        {
            throw new InvalidDataException("Duplicate celestial calendar country.");
        }

        var validCodes = countries.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
        var holidays = raw.Holidays.Select(item =>
        {
            var date = ParseDate(item.Date);
            if (date < start || date > end || !validCodes.Contains(item.Country)
                || item.Kind is not ("holiday" or "workday")
                || item.Quality is not ("rule_based" or "estimated" or "provisional")
                || string.IsNullOrWhiteSpace(item.Name) || !IsArtworkFileName(item.ArtworkFileName)
                || !IsHttps(item.SourceUrl))
                throw new InvalidDataException("Invalid celestial calendar holiday entry.");
            return new CelestialCalendarHoliday(date, item.Country, item.Name, item.Kind,
                item.Quality, item.ArtworkFileName, item.SourceUrl);
        }).ToArray();
        if (holidays.Select(item => (item.Date, item.Country, item.Kind, item.Name)).Distinct().Count() != holidays.Length)
        {
            throw new InvalidDataException("Duplicate celestial calendar holiday entry.");
        }

        var saints = raw.Saints.Select(item =>
        {
            _ = new DateOnly(2000, item.Month, item.Day);
            if (string.IsNullOrWhiteSpace(item.EventKey) || string.IsNullOrWhiteSpace(item.NameLatin)
                || !IsHttps(item.SourceUrl))
                throw new InvalidDataException("Invalid celestial calendar saint entry.");
            return new CelestialCalendarSaint(item.Month, item.Day, item.EventKey,
                item.NameLatin, item.SourceUrl);
        }).ToArray();
        if (saints.Select(item => item.EventKey).Distinct(StringComparer.Ordinal).Count() != saints.Length
            || saints.Select(item => (item.Month, item.Day)).Distinct().Count() != 191)
        {
            throw new InvalidDataException("The celestial calendar saint catalog contains duplicate or missing entries.");
        }

        return new CelestialCalendarDataset(
            Convert.ToHexString(SHA256.HashData(bytes)), start, end, raw.HolidayLibrary,
            raw.SaintsSource.Revision, Array.AsReadOnly(countries), Array.AsReadOnly(holidays), Array.AsReadOnly(saints));
    }

    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new InvalidDataException($"Invalid celestial calendar date: {value}");

    private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsArtworkFileName(string value) => value.EndsWith(".png", StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private sealed record RawDataset(
        int SchemaVersion, string CoverageStart, string CoverageEnd, string HolidayLibrary,
        string HolidayScope, RawSaintSource SaintsSource,
        IReadOnlyList<RawCountry> Countries, IReadOnlyList<RawHoliday> Holidays,
        IReadOnlyList<RawSaint> Saints);

    private sealed record RawSaintSource(string Source, string Revision, string Scope, int DatesCovered, int Entries);
    private sealed record RawCountry(string Code, string Name, string Scope, string SourceUrl);
    private sealed record RawHoliday(
        string Date, string Country, string Name, string Kind, string Quality,
        string ArtworkFileName, string SourceUrl);
    private sealed record RawSaint(int Month, int Day, string EventKey, string NameLatin, string SourceUrl);
}
