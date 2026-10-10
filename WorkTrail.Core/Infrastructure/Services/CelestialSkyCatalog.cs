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

/// <summary>Loads and validates the embedded, source-attributed stellar and satellite identity catalog once.</summary>
internal static class CelestialSkyCatalog
{
    private static readonly Lazy<SkyCatalog> Data = new(Load);

    internal static SkyCatalog Current => Data.Value;

    private static SkyCatalog Load()
    {
        // An absent or corrupt embedded catalog is a packaging error, never an invitation to invent sky coordinates.
        using var stream = typeof(CelestialSkyCatalog).Assembly.GetManifestResourceStream("WorkTrail.Data.sky-catalog.json")
            ?? throw new InvalidDataException("The embedded sky catalog is missing.");
        var catalog = JsonSerializer.Deserialize<SkyCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("The embedded sky catalog is empty.");
        if (catalog.SchemaVersion != 1 || !Uri.TryCreate(catalog.SourceUrl, UriKind.Absolute, out var stellarSource)
            || stellarSource.Scheme != Uri.UriSchemeHttps
            || !Uri.TryCreate(catalog.SatelliteElementsUrl, UriKind.Absolute, out var orbitSource)
            || orbitSource.Scheme != Uri.UriSchemeHttps
            || catalog.Stars is null || catalog.Constellations is null || catalog.Satellites is null
            || catalog.ConjunctionBodies is null)
            throw new InvalidDataException("The sky catalog schema or source is unsupported.");

        var stars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var star in catalog.Stars)
        {
            if (star is null || string.IsNullOrWhiteSpace(star.Id) || string.IsNullOrWhiteSpace(star.Name)
                || !stars.Add(star.Id) || !double.IsFinite(star.RightAscensionDegrees)
                || star.RightAscensionDegrees is < 0 or >= 360
                || !double.IsFinite(star.DeclinationDegrees) || star.DeclinationDegrees is < -90 or > 90
                || star.Magnitude is { } magnitude && !double.IsFinite(magnitude))
                throw new InvalidDataException("The sky catalog contains an invalid or duplicate star.");
            // Apparent magnitude is not a visibility cutoff: valid catalog stars can be fainter than magnitude eight.
        }

        var figures = new HashSet<string>(StringComparer.Ordinal);
        var signs = new HashSet<TropicalZodiacSign>();
        foreach (var figure in catalog.Constellations)
        {
            if (figure is null || string.IsNullOrWhiteSpace(figure.Id) || !figures.Add(figure.Id)
                || figure.Segments is null || figure.Segments.Count == 0)
                throw new InvalidDataException("The sky catalog contains an invalid or duplicate constellation.");
            if (figure.ZodiacSign is not null && (!Enum.TryParse<TropicalZodiacSign>(figure.ZodiacSign, out var sign)
                    || !Enum.IsDefined(sign) || figure.Id != figure.ZodiacSign || !signs.Add(sign)))
                throw new InvalidDataException("The sky catalog contains an invalid zodiac figure.");
            foreach (var segment in figure.Segments)
            {
                if (segment is null || segment.Count != 2 || segment[0] == segment[1]
                    || !stars.Contains(segment[0]) || !stars.Contains(segment[1]))
                    throw new InvalidDataException("A constellation segment references an absent or repeated star.");
            }
        }

        var satelliteIds = new HashSet<string>(StringComparer.Ordinal);
        var satelliteNumbers = new HashSet<int>();
        foreach (var satellite in catalog.Satellites)
        {
            if (satellite is null || string.IsNullOrWhiteSpace(satellite.Id)
                || !satelliteIds.Add(satellite.Id) || satellite.NoradCatalogNumber is < 1 or > 99999
                || !satelliteNumbers.Add(satellite.NoradCatalogNumber))
                throw new InvalidDataException("The sky catalog contains an invalid or duplicate satellite identity.");
        }

        if (stars.Count == 0 || figures.Count == 0 || signs.Count != Enum.GetValues<TropicalZodiacSign>().Length
            || satelliteIds.Count == 0)
            throw new InvalidDataException("The embedded sky catalog is incomplete.");

        var conjunctionBodies = new HashSet<CelestialBodyKind>();
        foreach (var name in catalog.ConjunctionBodies)
        {
            if (!Enum.TryParse<CelestialBodyKind>(name, out var body) || !Enum.IsDefined(body)
                || body is CelestialBodyKind.Sun or CelestialBodyKind.Moon || !conjunctionBodies.Add(body))
                throw new InvalidDataException("The sky catalog contains an invalid conjunction body.");
        }
        if (conjunctionBodies.Count == 0)
            throw new InvalidDataException("The sky catalog contains no conjunction bodies.");

        return catalog;
    }

    internal sealed record SkyCatalog(int SchemaVersion, string SourceUrl, string SatelliteElementsUrl, IReadOnlyList<string> ConjunctionBodies, IReadOnlyList<SkyStar> Stars,
        IReadOnlyList<SkyConstellation> Constellations, IReadOnlyList<SkySatellite> Satellites);
    internal sealed record SkyStar(string Id, string Name, double RightAscensionDegrees, double DeclinationDegrees, double? Magnitude);
    internal sealed record SkyConstellation(string Id, string? ZodiacSign, IReadOnlyList<IReadOnlyList<string>> Segments);
    internal sealed record SkySatellite(string Id, int NoradCatalogNumber);
}
