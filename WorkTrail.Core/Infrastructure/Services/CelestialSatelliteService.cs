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


using System.Net;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using SGPdotNET.Util;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Projects catalog satellites from recent public orbital elements into the observer's local sky.</summary>
internal static class CelestialSatelliteService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan MaximumEpochAge = TimeSpan.FromDays(3);
    private static readonly TimeSpan CurrentSkyWindow = TimeSpan.FromHours(2);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static DateTimeOffset? _lastRefreshAttemptUtc;
    private static bool _requestDisabled;
    private static IReadOnlyDictionary<int, Tle> _elementsByCatalogNumber = new Dictionary<int, Tle>();

    /// <summary>Returns above-horizon positions only when the requested instant and orbital elements are recent.</summary>
    internal static async Task<IReadOnlyList<CelestialSatellitePosition>> GetPositionsAsync(
        CelestialSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var instant = snapshot.InstantUtc.ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        if ((instant - now).Duration() > CurrentSkyWindow)
            return [];

        var catalog = CelestialSkyCatalog.Current;
        await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_requestDisabled)
                return [];
            now = DateTimeOffset.UtcNow;
            if (_lastRefreshAttemptUtc is null || now - _lastRefreshAttemptUtc >= RefreshInterval)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Once sent, finish and cache this shared fetch even if its requesting view is resized or closed.
                // Otherwise rapid cancellations could repeatedly query the public orbital feed.
                _lastRefreshAttemptUtc = now;
                try
                {
                    using var response = await Http.GetAsync(catalog.SatelliteElementsUrl, CancellationToken.None).ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        // CelesTrak asks clients to stop requesting this feed after a non-200 response.
                        _requestDisabled = true;
                        _elementsByCatalogNumber = new Dictionary<int, Tle>();
                        return [];
                    }
                    var content = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var parsed = Tle.ParseElements(lines, true);
                    var approvedNumbers = catalog.Satellites.Select(satellite => satellite.NoradCatalogNumber).ToHashSet();
                    var approvedElements = parsed
                        .Where(element => approvedNumbers.Contains(checked((int)element.NoradNumber)))
                        .GroupBy(element => checked((int)element.NoradNumber))
                        .ToDictionary(group => group.Key, group => group.MaxBy(element => element.Epoch)!);
                    _elementsByCatalogNumber = approvedElements;
                }
                catch (Exception error) when (error is (HttpRequestException or IOException or FormatException or ArgumentException
                        or OverflowException or SGPdotNET.Exception.TleException or OperationCanceledException))
                {
                    // Transient failures still count toward the two-hour request interval.
                    _elementsByCatalogNumber = new Dictionary<int, Tle>();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var station = new GroundStation(new GeodeticCoordinate(
                Angle.FromDegrees(snapshot.Latitude), Angle.FromDegrees(snapshot.Longitude), 0));
            var positions = new List<CelestialSatellitePosition>();
            foreach (var identity in catalog.Satellites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_elementsByCatalogNumber.TryGetValue(identity.NoradCatalogNumber, out var elements))
                    continue;

                var epoch = new DateTimeOffset(DateTime.SpecifyKind(elements.Epoch, DateTimeKind.Utc));
                if ((instant - epoch).Duration() > MaximumEpochAge)
                    continue;

                try
                {
                    var observation = station.Observe(new Satellite(elements), instant.UtcDateTime);
                    var altitude = observation.Elevation.Degrees;
                    var azimuth = observation.Azimuth.Degrees;
                    if (double.IsFinite(altitude) && double.IsFinite(azimuth) && altitude >= 0)
                        positions.Add(new CelestialSatellitePosition(identity.Id, altitude, azimuth, epoch));
                }
                catch (SGPdotNET.Exception.SatellitePropagationException)
                {
                    // A failed propagation is not a valid apparent position; other catalog satellites can still render.
                }
            }

            return positions.AsReadOnly();
        }
        finally
        {
            RefreshGate.Release();
        }
    }
}
