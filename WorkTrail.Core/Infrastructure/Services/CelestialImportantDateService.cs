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
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Loads a strict local catalog of recurring important dates and projects its next occurrences into the observer's time zone.</summary>
internal static class CelestialImportantDateService
{
    private static readonly Lazy<ImportantDateCatalog> Data = new(LoadCatalog);

    /// <summary>Returns local all-day dates occurring in the next 367 calendar days, including only the next annual occurrence of each entry.</summary>
    internal static IReadOnlyList<CelestialAgendaEvent> BuildUpcoming(DateTimeOffset instantUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var localStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instantUtc, zone).DateTime);
        var localEnd = localStart.AddDays(367);
        var events = new List<CelestialAgendaEvent>();
        foreach (var item in Data.Value.Dates)
        {
            for (var year = localStart.Year; year <= localStart.Year + 1; year++)
            {
                var date = new DateOnly(year, item.Month, item.Day);
                if (date < localStart || date > localEnd)
                {
                    continue;
                }

                var localMidnight = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone);
                var startUtc = new DateTimeOffset(utc, TimeSpan.Zero);
                events.Add(new CelestialAgendaEvent(CelestialEventKind.ImportantDate, startUtc, null,
                    TimeZoneInfo.ConvertTime(startUtc, zone), null, ImportantDateId: item.Id));
            }
        }

        return Array.AsReadOnly(events.OrderBy(item => item.StartUtc).ToArray());
    }

    private static ImportantDateCatalog LoadCatalog()
    {
        using var stream = typeof(CelestialImportantDateService).Assembly.GetManifestResourceStream("WorkTrail.Data.important-dates.json")
            ?? throw new InvalidDataException("The embedded important-date catalog is missing.");
        var catalog = JsonSerializer.Deserialize<ImportantDateCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("The embedded important-date catalog is empty.");
        if (catalog.SchemaVersion != 1 || catalog.Dates is null || catalog.Dates.Count == 0)
        {
            throw new InvalidDataException("The important-date catalog metadata is unsupported.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalog.Dates)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)
                || !item.Id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
                || item.Month is < 1 or > 12)
            {
                throw new InvalidDataException("The important-date catalog contains an invalid or duplicate entry.");
            }

            try
            {
                _ = new DateOnly(2001, item.Month, item.Day);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException("An important-date catalog entry has an invalid annual date.", exception);
            }
        }

        return catalog with { Dates = Array.AsReadOnly(catalog.Dates.ToArray()) };
    }

    private sealed record ImportantDateCatalog(int SchemaVersion, IReadOnlyList<ImportantDate> Dates);
    private sealed record ImportantDate(string Id, int Month, int Day);
}
