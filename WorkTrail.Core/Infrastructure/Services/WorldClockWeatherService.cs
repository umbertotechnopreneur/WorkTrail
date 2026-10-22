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

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Defines current-weather retrieval and credential validation without exposing secrets.</summary>
internal interface IWorldClockWeatherProvider
{

    #region Methods and properties
    string Name { get; }

    string ConfigurationState { get; }

    bool IsConfigured { get; }

    /// <summary>Captures provider availability and its diagnostic configuration state.</summary>
    WorldClockWeatherProviderConfiguration CaptureConfiguration() =>
        new(IsConfigured, ConfigurationState);

    /// <summary>Checks a candidate credential directly with the provider.</summary>
    Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
        string secret,
        CancellationToken cancellationToken);

    /// <summary>Retrieves a source observation for the selected catalog coordinates.</summary>
    Task<WorldClockWeatherObservation> GetCurrentAsync(
        WorldClockWeatherLocation location,
        CancellationToken cancellationToken);

    #endregion

}

/// <summary>Captures provider availability without including its secret.</summary>
internal readonly record struct WorldClockWeatherProviderConfiguration(
    bool IsConfigured,
    string State);

/// <summary>Distinguishes accepted, rejected, rate-limited, and unavailable key checks.</summary>
internal enum WorldClockWeatherApiKeyValidation
{
    Accepted,
    Rejected,
    RateLimited,
    Unavailable
}

/// <summary>Identifies the catalog city and coordinates used for a weather request.</summary>
internal sealed record WorldClockWeatherLocation(
    string CityId,
    double Latitude,
    double Longitude);

/// <summary>Contains the provider's temperature, condition, and observation timestamp.</summary>
internal sealed record WorldClockWeatherObservation(
    double TemperatureCelsius,
    string ConditionKey,
    DateTimeOffset ObservedAtUtc);

/// <summary>Pairs available city observations with aggregate provider status.</summary>
internal sealed record WorldClockWeatherLoadResult(
    IReadOnlyDictionary<string, WorldClockWeather> Observations,
    WorldClockWeatherStatus Status);

/// <summary>Captures an observation and its cache and configuration generations.</summary>
internal readonly record struct WorldClockWeatherCacheEntry(
    WorldClockWeatherObservation Observation,
    DateTimeOffset CachedAtUtc,
    long Generation,
    long ConfigurationGeneration);

/// <summary>Owns source observations for a strict, timer-backed in-memory retention window.</summary>
internal sealed class WorldClockWeatherCache : IDisposable
{

    #region Fields
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _nextGeneration;
    private int _disposed;

    #endregion

    #region Initialization

    /// <summary>Configures observation expiry with a supplied time provider.</summary>
    internal WorldClockWeatherCache(TimeProvider timeProvider, TimeSpan retention)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _retention = retention > TimeSpan.Zero && retention != Timeout.InfiniteTimeSpan
            ? retention
            : throw new ArgumentOutOfRangeException(nameof(retention));
    }

    #endregion

    #region Methods and properties

    internal int Count => _entries.Count;

    /// <summary>Reads the retained observation and its generation without refreshing its lifetime.</summary>
    internal bool TryGet(string cityId, out WorldClockWeatherCacheEntry entry)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_entries.TryGetValue(cityId, out var current))
        {
            entry = new WorldClockWeatherCacheEntry(
                current.Observation,
                current.CachedAtUtc,
                current.Generation,
                current.ConfigurationGeneration);
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>Replaces a city observation and schedules generation-specific expiration.</summary>
    internal void Set(
        string cityId,
        WorldClockWeatherObservation observation,
        DateTimeOffset cachedAtUtc,
        long configurationGeneration = 0,
        TimeSpan? retention = null)
    {
        var entryRetention = retention ?? _retention;
        if (entryRetention <= TimeSpan.Zero || entryRetention == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var generation = Interlocked.Increment(ref _nextGeneration);
            var timer = _timeProvider.CreateTimer(
                static state =>
                {
                    var expiration = (ExpirationState)state!;
                    expiration.Owner.Remove(expiration.CityId, expiration.Generation);
                },
                new ExpirationState(this, cityId, generation),
                entryRetention,
                Timeout.InfiniteTimeSpan);
            var replacement = new Entry(observation, cachedAtUtc, generation, configurationGeneration, timer);
            Entry? previous = null;
            try
            {
                _entries.AddOrUpdate(
                    cityId,
                    replacement,
                    (_, current) =>
                    {
                        previous = current;
                        return replacement;
                    });
            }
            catch
            {
                timer.Dispose();
                throw;
            }

            previous?.ExpirationTimer.Dispose();
        }
    }

    /// <summary>Removes only the matching generation so an old timer cannot evict a replacement.</summary>
    internal void Remove(string cityId, long generation)
    {
        // Match both generation and entry identity: an expired timer must not remove a newer observation.
        if (_entries.TryGetValue(cityId, out var current)
            && current.Generation == generation
            && ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(
                new KeyValuePair<string, Entry>(cityId, current)))
        {
            current.ExpirationTimer.Dispose();
        }
    }

    /// <summary>Removes observations and disposes their expiration timers under the lifecycle gate.</summary>
    internal void Clear()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            foreach (var pair in _entries.ToArray())
            {
                if (((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(pair))
                {
                    pair.Value.ExpirationTimer.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Disposes retained observations and their expiration timers.
    /// </summary>
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var pair in _entries.ToArray())
            {
                if (((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(pair))
                {
                    pair.Value.ExpirationTimer.Dispose();
                }
            }
        }
    }

    #endregion

    #region Nested types

    /// <summary>Owns a retained observation and the timer that expires its generation.</summary>
    private sealed record Entry(
        WorldClockWeatherObservation Observation,
        DateTimeOffset CachedAtUtc,
        long Generation,
        long ConfigurationGeneration,
        ITimer ExpirationTimer);

    /// <summary>Identifies the cache generation owned by an expiration callback.</summary>
    private sealed record ExpirationState(
        WorldClockWeatherCache Owner,
        string CityId,
        long Generation);

    #endregion

}

/// <summary>Loads bounded, source-backed current weather without making clocks depend on network availability.</summary>
internal sealed class WorldClockWeatherService : IDisposable
{

    #region Fields
    /// <summary>Elapsed cache time after which the last valid observation is revalidated.</summary>
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(12);

    /// <summary>Maximum accepted age of a provider observation.</summary>
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromMinutes(45);

    /// <summary>Maximum accepted provider clock lead relative to this device.</summary>
    internal static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);

    private readonly IWorldClockWeatherProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly WorldClockWeatherCache _cache;
    private readonly ConcurrentDictionary<WeatherFetchKey, Lazy<Task<WeatherFetchOutcome>>> _inFlight = new();
    private readonly CancellationTokenSource _serviceCancellation = new();
    private readonly CancellationToken _serviceToken;
    private long _configurationGeneration;
    private int _disposed;

    #endregion

    #region Initialization

    /// <summary>Connects provider retrieval to bounded caching, logging, and service cancellation.</summary>
    internal WorldClockWeatherService(
        IWorldClockWeatherProvider provider,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _cache = new WorldClockWeatherCache(
            _timeProvider,
            MaximumObservationAge + MaximumFutureSkew);
        _serviceToken = _serviceCancellation.Token;
    }

    #endregion

    #region Methods and properties

    internal int CachedObservationCount => _cache.Count;

    /// <summary>Captures the provider's current availability without its credentials.</summary>
    internal WorldClockWeatherProviderConfiguration CaptureConfiguration() =>
        _provider.CaptureConfiguration();

    /// <summary>Forwards credential validation to the configured provider.</summary>
    internal Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
        string secret,
        CancellationToken cancellationToken) =>
        _provider.ValidateApiKeyAsync(secret, cancellationToken);

    /// <summary>Advances configuration identity and discards observations from the previous key.</summary>
    internal void InvalidateConfiguration()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Interlocked.Increment(ref _configurationGeneration);
        _cache.Clear();
    }

    /// <summary>Loads selected city observations and reports complete, partial, or unavailable weather.</summary>
    internal async Task<WorldClockWeatherLoadResult> LoadCurrentAsync(
        IReadOnlyList<WorldClockWeatherLocation> locations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (locations.Count is < 1 or > WorldClockSelection.MaximumClocks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(locations),
                $"Current weather requires between 1 and {WorldClockSelection.MaximumClocks} selected cities.");
        }

        var providerConfiguration = _provider.CaptureConfiguration();
        if (!providerConfiguration.IsConfigured)
        {
            var configurationState = providerConfiguration.State;
            var unavailableState = configurationState switch
            {
                "missing-api-key" => "configuration-required",
                "invalid-api-key" => "configuration-required",
                "environment-unavailable" => "unavailable",
                _ => "unavailable"
            };
            return new WorldClockWeatherLoadResult(
                new Dictionary<string, WorldClockWeather>(StringComparer.Ordinal),
                new WorldClockWeatherStatus(
                    _provider.Name,
                    unavailableState,
                    configurationState,
                    locations.Count,
                    0,
                    IsProviderConfigured: false));
        }

        var outcomes = await Task.WhenAll(locations.Select(location =>
            LoadLocationAsync(location, cancellationToken))).ConfigureAwait(false);
        var observations = outcomes
            .Where(static outcome => outcome.Weather is not null)
            .ToDictionary(
                static outcome => outcome.CityId,
                static outcome => outcome.Weather!,
                StringComparer.Ordinal);
        var reasonCodes = outcomes
            .Where(static outcome => outcome.Weather is null)
            .Select(static outcome => outcome.ReasonCode)
            .Where(static reason => reason is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var state = observations.Count switch
        {
            var count when count == locations.Count => "available",
            > 0 => "partial",
            _ => "unavailable"
        };
        var reasonCode = reasonCodes.Length switch
        {
            0 => null,
            1 => reasonCodes[0],
            _ => "mixed-failures"
        };
        return new WorldClockWeatherLoadResult(
            observations,
            new WorldClockWeatherStatus(
                _provider.Name,
                state,
                reasonCode,
                locations.Count,
                observations.Count,
                IsProviderConfigured: true));
    }

    /// <summary>Removes observations that expired before the clock snapshot was projected.</summary>
    internal WorldClockWeatherLoadResult RevalidateForSnapshot(
        WorldClockWeatherLoadResult result,
        DateTimeOffset snapshotInstantUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Observations.Count == 0)
        {
            return result;
        }

        var observations = result.Observations
            .Select(pair => new KeyValuePair<string, WorldClockWeather>(
                pair.Key,
                pair.Value with
                {
                    IsFresh = IsFreshAt(pair.Value.ObservedAtUtc, snapshotInstantUtc)
                }))
            .Where(static pair => pair.Value.IsFresh)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        if (observations.Count == result.Observations.Count)
        {
            return result;
        }

        var state = observations.Count switch
        {
            var count when count == result.Status.RequestedCities => "available",
            > 0 => "partial",
            _ => "unavailable"
        };
        var reasonCode = result.Status.ReasonCode switch
        {
            null => "stale-observation",
            "stale-observation" => "stale-observation",
            _ => "mixed-failures"
        };
        return new WorldClockWeatherLoadResult(
            observations,
            result.Status with
            {
                State = state,
                ReasonCode = reasonCode,
                AvailableObservations = observations.Count
            });
    }

    #endregion

    #region Implementation

    /// <summary>Returns valid cached weather and starts shared revalidation when needed.</summary>
    private async Task<WeatherFetchOutcome> LoadLocationAsync(
        WorldClockWeatherLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configurationGeneration = Volatile.Read(ref _configurationGeneration);
        var now = _timeProvider.GetUtcNow();
        if (TryGetCached(
                location.CityId,
                now,
                configurationGeneration,
                out var cached,
                out var requiresRevalidation))
        {
            if (requiresRevalidation)
            {
                // Stale-while-revalidate keeps a still-valid observation visible while optional I/O runs.
                _ = GetOrStartFetch(location, configurationGeneration);
            }

            return new WeatherFetchOutcome(location.CityId, ToContract(cached), null);
        }

        return await GetOrStartFetch(location, configurationGeneration)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Shares one provider fetch per city and configuration generation.</summary>
    private Task<WeatherFetchOutcome> GetOrStartFetch(
        WorldClockWeatherLocation location,
        long configurationGeneration)
    {
        var candidate = new Lazy<Task<WeatherFetchOutcome>>(
            () => FetchLocationAsync(location, configurationGeneration, _serviceToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var key = new WeatherFetchKey(location.CityId, configurationGeneration);
        var active = _inFlight.GetOrAdd(key, candidate);
        var sharedTask = active.Value;
        _ = sharedTask.ContinueWith(
            completed =>
            {
                // Observe a disposal cancellation/fault even when refresh runs entirely in the background.
                _ = completed.Exception;
                RemoveInFlight(key, active);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return sharedTask;
    }

    /// <summary>Fetches fresh weather while preserving a valid cached observation on provider failure.</summary>
    private async Task<WeatherFetchOutcome> FetchLocationAsync(
        WorldClockWeatherLocation location,
        long configurationGeneration,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (TryGetCached(
                location.CityId,
                now,
                configurationGeneration,
                out var cached,
                out var requiresRevalidation)
            && !requiresRevalidation)
        {
            return new WeatherFetchOutcome(location.CityId, ToContract(cached), null);
        }

        try
        {
            // Every waiter shares this optional request; caller cancellation only stops that caller's wait.
            var observation = await _provider.GetCurrentAsync(location, cancellationToken).ConfigureAwait(false);
            now = _timeProvider.GetUtcNow();
            if (!IsFreshAt(observation.ObservedAtUtc, now))
            {
                // A stale provider response must not evict a previously valid observation.
                return CurrentFallback(location.CityId, now, configurationGeneration, "stale-observation");
            }

            if (configurationGeneration == Volatile.Read(ref _configurationGeneration))
            {
                _cache.Set(
                    location.CityId,
                    observation,
                    now,
                    configurationGeneration,
                    RetentionFromObservation(observation.ObservedAtUtc, now));
            }

            return new WeatherFetchOutcome(location.CityId, ToContract(observation), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Log only a stable city identifier and exception type; provider URIs can contain the API key.
            _logger.LogWarning(
                "Current weather request failed. CityId={CityId} ExceptionType={ExceptionType}",
                location.CityId,
                exception.GetType().Name);
            // Transient provider failures preserve the last observation only while it is still valid.
            return CurrentFallback(
                location.CityId,
                _timeProvider.GetUtcNow(),
                configurationGeneration,
                "request-failed");
        }
    }

    /// <summary>Returns only a still-valid cached observation with the fetch failure reason.</summary>
    private WeatherFetchOutcome CurrentFallback(
        string cityId,
        DateTimeOffset now,
        long configurationGeneration,
        string reasonCode) =>
        TryGetCached(
            cityId,
            now,
            configurationGeneration,
            out var observation,
            out _)
            ? new WeatherFetchOutcome(cityId, ToContract(observation), reasonCode)
            : new WeatherFetchOutcome(cityId, null, reasonCode);

    /// <summary>Removes a completed fetch only if it still owns the dictionary entry.</summary>
    private void RemoveInFlight(
        WeatherFetchKey key,
        Lazy<Task<WeatherFetchOutcome>> operation) =>
        ((ICollection<KeyValuePair<WeatherFetchKey, Lazy<Task<WeatherFetchOutcome>>>>)_inFlight).Remove(
            new KeyValuePair<WeatherFetchKey, Lazy<Task<WeatherFetchOutcome>>>(key, operation));

    /// <summary>Checks configuration generation and observation freshness before returning cached weather.</summary>
    private bool TryGetCached(
        string cityId,
        DateTimeOffset now,
        long configurationGeneration,
        out WorldClockWeatherObservation observation,
        out bool requiresRevalidation)
    {
        if (_cache.TryGet(cityId, out var entry))
        {
            if (entry.ConfigurationGeneration != configurationGeneration)
            {
                observation = null!;
                requiresRevalidation = false;
                return false;
            }

            var cacheAge = now - entry.CachedAtUtc;
            if (IsFreshAt(entry.Observation.ObservedAtUtc, now))
            {
                observation = entry.Observation;
                requiresRevalidation = cacheAge < TimeSpan.Zero || cacheAge > CacheDuration;
                return true;
            }

            _cache.Remove(cityId, entry.Generation);
        }

        observation = null!;
        requiresRevalidation = false;
        return false;
    }

    /// <summary>Bounds cache lifetime by the source observation's expiry time.</summary>
    private static TimeSpan RetentionFromObservation(
        DateTimeOffset observedAtUtc,
        DateTimeOffset cachedAtUtc)
    {
        var retention = observedAtUtc.ToUniversalTime()
            + MaximumObservationAge
            - cachedAtUtc.ToUniversalTime();
        if (retention <= TimeSpan.Zero)
        {
            throw new InvalidDataException("A stale weather observation cannot be cached.");
        }

        return retention;
    }

    /// <summary>Checks maximum observation age and tolerated provider clock lead.</summary>
    private static bool IsFreshAt(DateTimeOffset observedAtUtc, DateTimeOffset nowUtc)
    {
        var age = nowUtc.ToUniversalTime() - observedAtUtc.ToUniversalTime();
        return age >= -MaximumFutureSkew && age < MaximumObservationAge;
    }

    /// <summary>Maps a validated provider observation into the public weather DTO.</summary>
    private static WorldClockWeather ToContract(WorldClockWeatherObservation observation) =>
        new(
            observation.TemperatureCelsius,
            observation.ConditionKey,
            observation.ObservedAtUtc.ToUniversalTime(),
            IsFresh: true);

    #endregion

    #region Nested types

    /// <summary>Carries a city's available weather and optional failure reason.</summary>
    private sealed record WeatherFetchOutcome(
        string CityId,
        WorldClockWeather? Weather,
        string? ReasonCode);

    /// <summary>Separates shared fetches by city and provider configuration generation.</summary>
    private readonly record struct WeatherFetchKey(string CityId, long ConfigurationGeneration);

    #endregion

    #region Methods and properties

    /// <summary>
    /// Cancels in-flight weather requests and releases the owned weather cache.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _serviceCancellation.Cancel();
        _cache.Dispose();
        _serviceCancellation.Dispose();
    }

    #endregion

}

/// <summary>Calls OpenWeather Current Weather with selected catalog coordinates and an environment-only key.</summary>
internal sealed class OpenWeatherCurrentProvider : IWorldClockWeatherProvider
{

    #region Fields
    internal const string ApiKeyEnvironmentVariable = "WORKTRAIL_OPENWEATHER_API_KEY";
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    internal const int MaximumResponseBytes = 64 * 1024;
    private const string Endpoint = "https://api.openweathermap.org/data/2.5/weather";
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private readonly HttpClient _httpClient;
    private readonly Func<(string? ApiKey, string State)> _configurationResolver;
    private readonly TimeSpan _requestTimeout;

    #endregion

    #region Initialization

    /// <summary>Configures bounded HTTP retrieval and the environment-backed credential resolver.</summary>
    private OpenWeatherCurrentProvider(
        HttpClient httpClient,
        Func<(string? ApiKey, string State)> configurationResolver,
        TimeSpan requestTimeout)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configurationResolver = configurationResolver ?? throw new ArgumentNullException(nameof(configurationResolver));
        _requestTimeout = requestTimeout > TimeSpan.Zero && requestTimeout != Timeout.InfiniteTimeSpan
            ? requestTimeout
            : throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    #endregion

    #region Methods and properties

    /// <inheritdoc />
    public string Name => "openweather";

    /// <inheritdoc />
    public string ConfigurationState => ResolveConfiguration().State;

    /// <inheritdoc />
    public bool IsConfigured
    {
        get
        {
            var configuration = ResolveConfiguration();
            return configuration.ApiKey is not null && configuration.State == "configured";
        }
    }

    /// <inheritdoc />
    public WorldClockWeatherProviderConfiguration CaptureConfiguration()
    {
        var configuration = ResolveConfiguration();
        return new WorldClockWeatherProviderConfiguration(
            configuration.ApiKey is not null && configuration.State == "configured",
            configuration.State);
    }

    /// <summary>Creates the production provider using environment-only credentials.</summary>
    internal static OpenWeatherCurrentProvider CreateFromEnvironment(HttpClient? httpClient = null) =>
        new(
            httpClient ?? SharedHttpClient,
            ReadEnvironmentConfiguration,
            DefaultRequestTimeout);

    /// <summary>Creates an isolated provider with a fixed credential configuration.</summary>
    internal static OpenWeatherCurrentProvider CreateForTests(
        HttpClient httpClient,
        string? apiKey,
        TimeSpan? requestTimeout = null)
    {
        var configuration = string.IsNullOrWhiteSpace(apiKey)
            ? (ApiKey: (string?)null, State: "missing-api-key")
            : (ApiKey: apiKey.Trim(), State: "configured");
        return new OpenWeatherCurrentProvider(
            httpClient,
            () => configuration,
            requestTimeout ?? DefaultRequestTimeout);
    }

    /// <summary>Creates an isolated provider with a replaceable configuration resolver.</summary>
    internal static OpenWeatherCurrentProvider CreateDynamicForTests(
        HttpClient httpClient,
        Func<(string? ApiKey, string State)> configurationResolver,
        TimeSpan? requestTimeout = null) =>
        new(
            httpClient,
            configurationResolver,
            requestTimeout ?? DefaultRequestTimeout);

    /// <summary>Checks credential length and printable ASCII before any provider request.</summary>
    internal static bool IsPlausibleApiKey(string? value) =>
        value is { Length: >= 16 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');

    /// <inheritdoc />
    public async Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
        string secret,
        CancellationToken cancellationToken)
    {
        if (!IsPlausibleApiKey(secret))
        {
            return WorldClockWeatherApiKeyValidation.Rejected;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildRequestUri(51.5074d, -0.1278d, secret));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            // Only the status code is needed; the key-bearing request URI is never logged or returned.
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            return response.StatusCode switch
            {
                >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices =>
                    WorldClockWeatherApiKeyValidation.Accepted,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    WorldClockWeatherApiKeyValidation.Rejected,
                HttpStatusCode.TooManyRequests =>
                    WorldClockWeatherApiKeyValidation.RateLimited,
                _ => WorldClockWeatherApiKeyValidation.Unavailable
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return WorldClockWeatherApiKeyValidation.Unavailable;
        }
        catch (HttpRequestException)
        {
            return WorldClockWeatherApiKeyValidation.Unavailable;
        }
    }

    /// <inheritdoc />
    public async Task<WorldClockWeatherObservation> GetCurrentAsync(
        WorldClockWeatherLocation location,
        CancellationToken cancellationToken)
    {
        var configuration = ResolveConfiguration();
        if (configuration.ApiKey is null || configuration.State != "configured")
        {
            throw new InvalidOperationException("The current-weather provider is not configured.");
        }

        var requestUri = BuildRequestUri(location.Latitude, location.Longitude, configuration.ApiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        var requestToken = timeout.Token;
        // The API key is used only for this direct HTTPS request and is never logged or persisted.
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Current weather provider returned HTTP {(int)response.StatusCode}.");
            }

            var payload = await ReadBoundedContentAsync(response.Content, requestToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions { MaxDepth = 32 });
            return ParseObservation(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Hide the key-bearing request URI from the optional service's failure surface.
            throw new InvalidOperationException("Current weather provider request timed out.");
        }
        catch (HttpRequestException)
        {
            // Hide the key-bearing request URI from the optional service's failure surface.
            throw new InvalidOperationException("Current weather provider request failed.");
        }
    }

    #endregion

    #region Implementation

    /// <summary>Encodes coordinates and the credential for the direct provider request.</summary>
    private static Uri BuildRequestUri(double latitude, double longitude, string apiKey)
    {
        var latitudeText = latitude.ToString("0.######", CultureInfo.InvariantCulture);
        var longitudeText = longitude.ToString("0.######", CultureInfo.InvariantCulture);
        var key = Uri.EscapeDataString(apiKey);
        return new Uri($"{Endpoint}?lat={latitudeText}&lon={longitudeText}&appid={key}&units=metric");
    }

    /// <summary>Reads the response with a strict byte cap, including unknown-length bodies.</summary>
    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("Current weather response exceeds the supported size.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var remainingWithOverflowByte = MaximumResponseBytes - checked((int)buffer.Length) + 1;
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, Math.Min(chunk.Length, remainingWithOverflowByte)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("Current weather response exceeds the supported size.");
            }

            buffer.Write(chunk, 0, read);
        }
    }

    #endregion

    #region Methods and properties

    /// <summary>Validates metric temperature, source time, and condition identifiers.</summary>
    internal static WorldClockWeatherObservation ParseObservation(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("main", out var main)
            || main.ValueKind != JsonValueKind.Object
            || !main.TryGetProperty("temp", out var temperatureElement)
            || !temperatureElement.TryGetDouble(out var temperature)
            || !double.IsFinite(temperature)
            || temperature is < -150d or > 100d)
        {
            throw new InvalidDataException("Current weather response has an invalid metric temperature.");
        }

        if (!root.TryGetProperty("dt", out var timestampElement)
            || !timestampElement.TryGetInt64(out var unixTimestamp))
        {
            throw new InvalidDataException("Current weather response has no observation timestamp.");
        }

        DateTimeOffset observedAtUtc;
        try
        {
            observedAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Current weather response has an invalid observation timestamp.", exception);
        }

        if (!root.TryGetProperty("weather", out var weather)
            || weather.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Current weather response has no condition identifiers.");
        }

        var conditionIds = new List<int>();
        foreach (var condition in weather.EnumerateArray())
        {
            if (condition.ValueKind != JsonValueKind.Object
                || !condition.TryGetProperty("id", out var identifier)
                || identifier.ValueKind != JsonValueKind.Number
                || !identifier.TryGetInt32(out var conditionId))
            {
                throw new InvalidDataException("Current weather response contains an invalid condition identifier.");
            }

            conditionIds.Add(conditionId);
        }

        if (conditionIds.Count == 0)
        {
            throw new InvalidDataException("Current weather response has no valid condition identifiers.");
        }

        return new WorldClockWeatherObservation(
            temperature,
            MapCondition(conditionIds),
            observedAtUtc);
    }

    /// <summary>Chooses a supported decorative condition with severe weather taking precedence.</summary>
    internal static string MapCondition(IReadOnlyList<int> conditionIds)
    {
        ArgumentNullException.ThrowIfNull(conditionIds);
        if (conditionIds.Count == 0)
        {
            throw new ArgumentException("At least one weather condition identifier is required.", nameof(conditionIds));
        }

        if (conditionIds.Any(static id => !IsSupportedCondition(id)))
        {
            // Keep valid temperature/time data when the provider adds a condition we do not yet decorate.
            return "unknown";
        }

        if (conditionIds.Any(static id => IsLightning(id)))
        {
            return "lightning";
        }

        var hasRain = conditionIds.Any(static id => IsRain(id));
        var hasSnow = conditionIds.Any(static id => IsSnow(id));
        var hasMixedPrecipitation = conditionIds.Any(static id => IsMixedPrecipitation(id));
        if ((hasRain && hasSnow) || hasMixedPrecipitation)
        {
            return "mixed-precipitation";
        }

        if (hasSnow)
        {
            return "snow";
        }

        if (hasRain)
        {
            return "rain";
        }

        if (conditionIds.Any(static id => id is 701 or 741))
        {
            return "fog";
        }

        if (conditionIds.Any(static id => id is >= 801 and <= 804))
        {
            return "cloudy";
        }

        if (conditionIds.All(static id => id == 800))
        {
            return "clear";
        }

        return "unknown";
    }

    #endregion

    #region Implementation

    /// <summary>Checks whether a provider condition has a known decoration.</summary>
    private static bool IsSupportedCondition(int id) =>
        IsLightning(id)
        || IsRain(id)
        || IsSnow(id)
        || IsMixedPrecipitation(id)
        || id is 701 or 741 or >= 800 and <= 804;

    /// <summary>Recognizes the provider's thunderstorm condition identifiers.</summary>
    private static bool IsLightning(int id) => id is
        200 or 201 or 202 or 210 or 211 or 212 or 221 or 230 or 231 or 232;

    /// <summary>Recognizes the provider's drizzle and rain condition identifiers.</summary>
    private static bool IsRain(int id) => id is
        300 or 301 or 302 or 310 or 311 or 312 or 313 or 314 or 321 or
        500 or 501 or 502 or 503 or 504 or 511 or 520 or 521 or 522 or 531;

    /// <summary>Recognizes the provider's snow condition identifiers.</summary>
    private static bool IsSnow(int id) => id is 600 or 601 or 602 or 620 or 621 or 622;

    /// <summary>Recognizes the provider's sleet and mixed precipitation identifiers.</summary>
    private static bool IsMixedPrecipitation(int id) => id is 611 or 612 or 613 or 615 or 616;

    /// <summary>Rejects malformed credentials returned by the configured resolver.</summary>
    private (string? ApiKey, string State) ResolveConfiguration()
    {
        var (apiKey, state) = _configurationResolver();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, state);
        }

        return IsPlausibleApiKey(apiKey)
            ? (apiKey, state)
            : (null, "invalid-api-key");
    }

    /// <summary>Reads credentials in process, user, then machine scope without logging their values.</summary>
    private static (string? ApiKey, string State) ReadEnvironmentConfiguration()
    {
        try
        {
            foreach (var target in new[]
                     {
                         EnvironmentVariableTarget.Process,
                         EnvironmentVariableTarget.User,
                         EnvironmentVariableTarget.Machine
                     })
            {
                var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable, target);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    return IsPlausibleApiKey(apiKey)
                        ? (apiKey, "configured")
                        : (null, "invalid-api-key");
                }
            }

            return (null, "missing-api-key");
        }
        catch (SecurityException)
        {
            // Optional weather remains unavailable when Windows denies environment access; clocks remain local.
            return (null, "environment-unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            // Optional weather remains unavailable when Windows denies environment access; clocks remain local.
            return (null, "environment-unavailable");
        }
    }

    #endregion

}
