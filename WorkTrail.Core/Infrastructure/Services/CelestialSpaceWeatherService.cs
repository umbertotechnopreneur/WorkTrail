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


using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Retrieves and bounds NOAA SWPC public space-weather data for the live astronomical agenda.</summary>
internal static class CelestialSpaceWeatherService
{
    private static readonly Uri KpForecastUri = new("https://services.swpc.noaa.gov/products/noaa-planetary-k-index-forecast.json");
    private static readonly Uri AlertsUri = new("https://services.swpc.noaa.gov/products/alerts.json");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RelevantInstantWindow = TimeSpan.FromHours(48);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient Http = new() { Timeout = RequestTimeout };
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex KpPattern = new(@"Geomagnetic K-index of\s+(?<value>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ScalePattern = new(@"\b(?<family>[GSR])(?<value>[1-5])\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ValidityPattern = new(@"(?:Valid From|Valid To|Now Valid Until|Begin Time):\s*(?<value>\d{4}\s+[A-Za-z]{3}\s+\d{2}\s+\d{4})\s+UTC", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static DateTimeOffset? _lastRefreshAttemptUtc;
    private static SpaceWeatherPayload _payload = SpaceWeatherPayload.Empty;

    /// <summary>Returns a cached feed and converts significant global forecast or active conditions into city-local agenda events.</summary>
    internal static async Task<SpaceWeatherProjection> GetAsync(
        DateTimeOffset instantUtc,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        cancellationToken.ThrowIfCancellationRequested();
        var instant = instantUtc.ToUniversalTime();
        if ((instant - DateTimeOffset.UtcNow).Duration() > RelevantInstantWindow)
            return new SpaceWeatherProjection(SpaceWeatherSnapshot.Empty, []);

        var payload = await GetPayloadAsync(cancellationToken).ConfigureAwait(false);
        var localPayload = payload with
        {
            ActiveAlerts = Array.AsReadOnly(payload.ActiveAlerts
            .Where(alert => IsRelevantAlert(alert, instant)).ToArray())
        };
        var snapshot = new SpaceWeatherSnapshot(payload.RetrievedUtc, payload.KpForecast, localPayload.ActiveAlerts);
        return new SpaceWeatherProjection(snapshot, BuildAgenda(localPayload, instant, zone));
    }

    /// <summary>Returns the highest-severity current global condition for the compact annotation.</summary>
    internal static async Task<SpaceWeatherAlert?> GetCurrentSignificantAlertAsync(
        DateTimeOffset instantUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instant = instantUtc.ToUniversalTime();
        if ((instant - DateTimeOffset.UtcNow).Duration() > RelevantInstantWindow)
            return null;

        var payload = await GetPayloadAsync(cancellationToken).ConfigureAwait(false);
        return payload.ActiveAlerts
            .Where(alert => IsRelevantAlert(alert, instant))
            .Where(alert => alert.NoaaScale is not null || alert.Kind == SpaceWeatherEventKind.HighEnergyElectronFlux)
            .OrderByDescending(alert => alert.NoaaScale ?? 0)
            .ThenByDescending(alert => alert.IssuedUtc)
            .FirstOrDefault();
    }

    private static async Task<SpaceWeatherPayload> GetPayloadAsync(CancellationToken cancellationToken)
    {
        await RefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastRefreshAttemptUtc is not null && now - _lastRefreshAttemptUtc < RefreshInterval)
                return _payload;

            // Complete one shared fetch even if the requesting view is closed, so cancellations cannot hammer NOAA's public endpoints.
            _lastRefreshAttemptUtc = now;
            try
            {
                var kpTask = GetJsonAsync<KpResponse[]>(KpForecastUri);
                var alertsTask = GetJsonAsync<AlertResponse[]>(AlertsUri);
                await Task.WhenAll(kpTask, alertsTask).ConfigureAwait(false);
                _payload = ParsePayload(kpTask.Result, alertsTask.Result, now);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or FormatException or OperationCanceledException)
            {
                // Space weather is an optional live annotation: a failed public request leaves no claimed condition for this refresh interval.
                _payload = SpaceWeatherPayload.Empty;
            }

            return _payload;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    private static async Task<T> GetJsonAsync<T>(Uri uri)
    {
        using var response = await Http.GetAsync(uri, CancellationToken.None).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"NOAA SWPC returned HTTP {(int)response.StatusCode} for {uri}.");
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(content, SerializerOptions, CancellationToken.None).ConfigureAwait(false)
            ?? throw new JsonException("NOAA SWPC returned an empty JSON payload.");
    }

    private static SpaceWeatherPayload ParsePayload(IEnumerable<KpResponse> kpRows, IEnumerable<AlertResponse> alertRows, DateTimeOffset retrievedUtc)
    {
        var kp = kpRows.Select(row => TryParseUtc(row.TimeTag, out var start)
                && double.IsFinite(row.Kp) && row.Kp is >= 0 and <= 9
                ? new SpaceWeatherKpForecast(start, row.Kp, row.Observed ?? "unknown", ParseScale(row.NoaaScale, 'G'))
                : null)
            .Where(row => row is not null)
            .Select(row => row!)
            .OrderBy(row => row.StartUtc)
            .ToArray();

        var alerts = alertRows.Select(row => ParseAlert(row, retrievedUtc))
            .Where(alert => alert is not null)
            .Select(alert => alert!)
            .OrderBy(alert => alert.ValidFromUtc ?? alert.IssuedUtc)
            .ToArray();
        return new SpaceWeatherPayload(retrievedUtc, Array.AsReadOnly(kp), Array.AsReadOnly(alerts));
    }

    private static SpaceWeatherAlert? ParseAlert(AlertResponse row, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(row.ProductId) || string.IsNullOrWhiteSpace(row.IssueDatetime) || string.IsNullOrWhiteSpace(row.Message)
            || !TryParseUtc(row.IssueDatetime, out var issued) || !TryClassify(row.Message, out var kind, out var scale, out var kp))
            return null;

        var validity = ValidityPattern.Matches(row.Message).Select(match => ParseAlertInstant(match.Groups["value"].Value)).Where(value => value is not null).Select(value => value!.Value).ToArray();
        DateTimeOffset? validFrom = row.Message.Contains("Begin Time:", StringComparison.OrdinalIgnoreCase) || row.Message.Contains("Valid From:", StringComparison.OrdinalIgnoreCase)
            ? (validity.Length > 0 ? validity[0] : null) : null;
        DateTimeOffset? validTo = row.Message.Contains("Valid To:", StringComparison.OrdinalIgnoreCase) || row.Message.Contains("Now Valid Until:", StringComparison.OrdinalIgnoreCase)
            ? (validity.Length > 0 ? validity[^1] : null) : null;
        if (!IsActive(row.Message, issued, validFrom, validTo, now)) return null;

        return new SpaceWeatherAlert(row.ProductId, issued, validFrom, validTo, kind, scale, kp, BuildSummary(kind, scale, kp));
    }

    private static IReadOnlyList<CelestialAgendaEvent> BuildAgenda(SpaceWeatherPayload payload, DateTimeOffset instant, TimeZoneInfo zone)
    {
        var events = new List<CelestialAgendaEvent>();
        var forecastEnd = instant.AddHours(48);
        var significantForecasts = payload.KpForecast
            .Where(period => IsRelevantForecast(period, instant))
            .OrderBy(period => period.StartUtc)
            .ToArray();

        foreach (var group in GroupConsecutiveForecasts(significantForecasts))
        {
            var start = group[0].StartUtc < instant ? instant : group[0].StartUtc;
            var end = group[^1].StartUtc.AddHours(3);
            if (end > forecastEnd) end = forecastEnd;
            var scale = group.Max(period => period.NoaaScale ?? GeomagneticScaleForKp(period.KpIndex));
            events.Add(new CelestialAgendaEvent(CelestialEventKind.SpaceWeather, start, end,
                TimeZoneInfo.ConvertTime(start, zone), TimeZoneInfo.ConvertTime(end, zone),
                IsApproximate: true, SpaceWeatherKind: SpaceWeatherEventKind.GeomagneticStorm,
                NoaaScale: scale, KpIndex: group.Max(period => period.KpIndex), SpaceWeatherAlertId: "noaa-kp-forecast"));
        }

        foreach (var alert in payload.ActiveAlerts.Where(alert => alert.NoaaScale is not null || alert.Kind == SpaceWeatherEventKind.HighEnergyElectronFlux))
        {
            var start = instant;
            events.Add(new CelestialAgendaEvent(CelestialEventKind.SpaceWeather, start, alert.ValidToUtc,
                TimeZoneInfo.ConvertTime(start, zone), alert.ValidToUtc is { } end ? TimeZoneInfo.ConvertTime(end, zone) : null,
                SpaceWeatherKind: alert.Kind, NoaaScale: alert.NoaaScale, KpIndex: alert.KpIndex,
                SpaceWeatherAlertId: alert.Id));
        }

        return Array.AsReadOnly(events.OrderBy(item => item.StartUtc).ThenBy(item => item.SpaceWeatherKind).ToArray());
    }

    /// <summary>Preserves forecast validity and storm significance without filtering by a city location.</summary>
    internal static bool IsRelevantForecast(SpaceWeatherKpForecast period, DateTimeOffset instant)
    {
        return string.Equals(period.Status, "predicted", StringComparison.OrdinalIgnoreCase)
            && period.StartUtc.AddHours(3) > instant && period.StartUtc < instant.AddHours(48)
            && double.IsFinite(period.KpIndex) && period.KpIndex is >= 5 and <= 9;
    }

    /// <summary>Never revives expired alerts and does not filter current NOAA conditions by city location.</summary>
    internal static bool IsRelevantAlert(SpaceWeatherAlert alert, DateTimeOffset instant)
    {
        // Recheck cached validity at the requested instant; an old 'Active Warning: YES' must not persist forever.
        if (alert.IssuedUtc > instant || alert.ValidFromUtc > instant
            || (alert.ValidToUtc is { } end ? end <= instant : alert.IssuedUtc.AddHours(24) <= instant)) return false;
        return alert.Kind != SpaceWeatherEventKind.GeomagneticStorm
            || (alert.KpIndex ?? (alert.NoaaScale is { } scale ? scale + 4d : 0d)) is >= 5 and <= 9;
    }

    private static IEnumerable<SpaceWeatherKpForecast[]> GroupConsecutiveForecasts(IReadOnlyList<SpaceWeatherKpForecast> periods)
    {
        var group = new List<SpaceWeatherKpForecast>();
        foreach (var period in periods)
        {
            if (group.Count > 0 && period.StartUtc - group[^1].StartUtc > TimeSpan.FromHours(3))
            {
                yield return group.ToArray();
                group.Clear();
            }
            group.Add(period);
        }
        if (group.Count > 0) yield return group.ToArray();
    }

    private static bool TryClassify(string message, out SpaceWeatherEventKind kind, out int? scale, out double? kp)
    {
        var scaleMatch = ScalePattern.Match(message);
        kp = KpPattern.Match(message) is { Success: true } kpMatch && double.TryParse(kpMatch.Groups["value"].Value, CultureInfo.InvariantCulture, out var parsedKp)
            ? parsedKp : null;
        if (message.Contains("Geomagnetic", StringComparison.OrdinalIgnoreCase) && (kp is not null || scaleMatch.Groups["family"].Value.Equals("G", StringComparison.OrdinalIgnoreCase)))
        {
            kind = SpaceWeatherEventKind.GeomagneticStorm;
            scale = scaleMatch.Groups["family"].Value.Equals("G", StringComparison.OrdinalIgnoreCase) ? int.Parse(scaleMatch.Groups["value"].Value, CultureInfo.InvariantCulture) : kp is { } value ? GeomagneticScaleForKp(value) : null;
            return true;
        }
        if (message.Contains("Radiation Storm", StringComparison.OrdinalIgnoreCase))
        {
            kind = SpaceWeatherEventKind.SolarRadiationStorm;
            scale = ParseScaleMatch(scaleMatch, 'S');
            return true;
        }
        if (message.Contains("Radio Blackout", StringComparison.OrdinalIgnoreCase))
        {
            kind = SpaceWeatherEventKind.RadioBlackout;
            scale = ParseScaleMatch(scaleMatch, 'R');
            return true;
        }
        if (message.Contains("Electron 2MeV", StringComparison.OrdinalIgnoreCase))
        {
            kind = SpaceWeatherEventKind.HighEnergyElectronFlux;
            scale = null;
            return true;
        }

        kind = default;
        scale = null;
        return false;
    }

    private static bool IsActive(string message, DateTimeOffset issued, DateTimeOffset? validFrom, DateTimeOffset? validTo, DateTimeOffset now) =>
        !message.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)
        && ((validTo is { } end && end >= now && (validFrom is null || validFrom <= now))
            || message.Contains("Active Warning: YES", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("WATCH:", StringComparison.OrdinalIgnoreCase) && issued >= now.AddDays(-3))
            || (message.Contains("CONTINUED ALERT:", StringComparison.OrdinalIgnoreCase) && issued >= now.AddHours(-30)));

    private static string BuildSummary(SpaceWeatherEventKind kind, int? scale, double? kp) => kind switch
    {
        SpaceWeatherEventKind.GeomagneticStorm when scale is { } value => $"NOAA G{value} geomagnetic storm",
        SpaceWeatherEventKind.GeomagneticStorm when kp is { } value => $"NOAA geomagnetic Kp {value.ToString("0.##", CultureInfo.InvariantCulture)}",
        SpaceWeatherEventKind.SolarRadiationStorm when scale is { } value => $"NOAA S{value} solar radiation storm",
        SpaceWeatherEventKind.RadioBlackout when scale is { } value => $"NOAA R{value} radio blackout",
        _ => "NOAA high-energy electron flux"
    };

    private static int? ParseScale(string? value, char family)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = ScalePattern.Match(value);
        return ParseScaleMatch(match, family);
    }

    private static int? ParseScaleMatch(Match match, char family) => match.Success && match.Groups["family"].Value.Equals(family.ToString(), StringComparison.OrdinalIgnoreCase)
        ? int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture) : null;

    private static int? GeomagneticScaleForKp(double kp) => kp < 5d ? null : Math.Clamp((int)Math.Floor(kp) - 4, 1, 5);

    private static bool TryParseUtc(string value, out DateTimeOffset instant) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant);

    private static DateTimeOffset? ParseAlertInstant(string value) => TryParseUtc(value, out var instant) ? instant : null;

    private sealed record SpaceWeatherPayload(DateTimeOffset RetrievedUtc, IReadOnlyList<SpaceWeatherKpForecast> KpForecast, IReadOnlyList<SpaceWeatherAlert> ActiveAlerts)
    {
        internal static SpaceWeatherPayload Empty { get; } = new(DateTimeOffset.MinValue, [], []);
    }

    internal sealed record SpaceWeatherProjection(SpaceWeatherSnapshot Snapshot, IReadOnlyList<CelestialAgendaEvent> Agenda);

    private sealed class KpResponse
    {
        [JsonPropertyName("time_tag")] public string TimeTag { get; init; } = string.Empty;
        [JsonPropertyName("kp")] public double Kp { get; init; }
        [JsonPropertyName("observed")] public string? Observed { get; init; }
        [JsonPropertyName("noaa_scale")] public string? NoaaScale { get; init; }
    }

    private sealed class AlertResponse
    {
        [JsonPropertyName("product_id")] public string ProductId { get; init; } = string.Empty;
        [JsonPropertyName("issue_datetime")] public string IssueDatetime { get; init; } = string.Empty;
        [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    }
}
