// SPDX-License-Identifier: MIT
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Runtime;
using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Constructs only the desktop widget services after runtime ownership is acquired.</summary>
public static class WorkTrailApplicationFactory
{
    /// <summary>Loads the stable installation identity without opening activity history.</summary>
    public static string LoadInstallationId() => new LocalStore().LoadSettings().InstallationId;

    /// <summary>Creates the local widget facade.</summary>
    public static IWorkTrailApplication Create(ILoggerFactory? loggerFactory = null, ObservabilityHealth? observability = null) =>
        new WorkTrailApplication(new LocalStore(), loggerFactory?.CreateLogger<WorkTrailApplication>(), observability,
            new WorldClockService(logger: loggerFactory?.CreateLogger<WorldClockService>()));
}

/// <summary>Owns desktop widget queries, serialized settings writes and Windows integration.</summary>
public sealed partial class WorkTrailApplication : IWorkTrailApplication
{
    private readonly LocalStore _store;
    private readonly SettingsSnapshot _settingsSnapshot;
    private readonly UtilityService _utilities;
    private readonly WindowSnappingService _windowSnapping = new();
    private readonly WindowStateService _windowState;
    private readonly WorldClockApplicationService _worldClockOperations;
    private readonly CelestialMapService _celestialMap = new();
    private readonly StartupService _startup = new();
    private readonly BuildInformationService _buildInformation = new();
    private readonly ApplicationLogService _applicationLogs = new();
    private readonly FeatureAccessPolicy _featureAccess;
    private readonly ILogger<WorkTrailApplication> _logger;
    private readonly ObservabilityHealth _observability;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private bool _disposed;
    private const string ProductRepositoryUrl = "https://github.com/umbertotechnopreneur/WorkTrail";
    private const string ProductIssuesUrl = ProductRepositoryUrl + "/issues";
    private const string ProductAuthorUrl = "https://umbertogiacobbi.biz/?utm_source=worktrail_app&utm_medium=referral&utm_campaign=worktrail&utm_content=about_author";
    private const string ProductPrivacyUrl = "https://umbertogiacobbi.biz/privacy/";
    private const string ProductTermsUrl = "https://umbertogiacobbi.biz/terms/";
    private const string OpenWeatherUrl = "https://openweathermap.org/";

    /// <summary>Creates a widget runtime with no hooks, screenshot pipeline, AI client or history store.</summary>
    public WorkTrailApplication(LocalStore store, ILogger<WorkTrailApplication>? logger = null,
        ObservabilityHealth? observability = null, WorldClockService? worldClockService = null,
        IFeatureLicenseSource? featureLicenseSource = null, UtilityService? utilities = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _utilities = utilities ?? new UtilityService();
        _settingsSnapshot = new SettingsSnapshot(store.LoadSettings());
        _windowState = new WindowStateService(store);
        _logger = logger ?? NullLogger<WorkTrailApplication>.Instance;
        _observability = observability ?? new ObservabilityHealth(false, false, "disabled", false);
        _featureAccess = new FeatureAccessPolicy(featureLicenseSource);
        _worldClockOperations = new WorldClockApplicationService(worldClockService ?? new WorldClockService(),
            _settingsSnapshot, _utilities.SetApiKey, PersistSettings);
    }

    /// <inheritdoc />
    public Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = RuntimeProtocol.CreateEndpoint(_settingsSnapshot.Value.InstallationId).PipeName["WorkTrail.Runtime.".Length..];
        return Task.FromResult(OperationResult<RuntimeHealth>.Success("runtime.healthy", "RuntimeHealthy",
            new RuntimeHealth(_utilities.GetAppVersion(), RuntimeProtocol.ProtocolVersion, fingerprint, true,
                ["world_clocks", "celestial", "window.state", "settings", "startup", "links", "diagnostics.logs"], _observability)));
    }

    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var validation = SettingsCatalog.Apply(_settingsSnapshot.Value, patch);
        if (!validation.Succeeded || validation.Value is null) return validation;
        var next = validation.Value;
        if (patch.Values.Keys.Any(key => key.Trim().Equals("home.city_id", StringComparison.OrdinalIgnoreCase)))
        {
            var catalog = _worldClockOperations.GetCatalog().Value!;
            if (!catalog.Cities.Any(city => city.Id == next.HomeCityId))
                return OperationResult<AppSettings>.Failure("settings.validation.failed", "SettingsValidationFailed",
                    new ValidationIssue("home.city_id", "not_found", "SettingsFieldInvalid"));
            var selected = WorldClockSelection.NormalizePersisted(next.WorldClockCityIds);
            if (!selected.Contains(next.HomeCityId) && selected.Count >= WorldClockSelection.MaximumClocks)
                return OperationResult<AppSettings>.Failure("world_clocks.limit", "WorldClocksMaximumReached");
            if (!selected.Contains(next.HomeCityId) && _featureAccess.DeniedWorldClockCreation(selected.Count) is { } denied)
                return OperationResult<AppSettings>.Failure(denied.Code, denied.MessageKey);
            next = next with
            {
                WorldClockCityIds = new[] { next.HomeCityId }.Concat(selected.Where(id => id != next.HomeCityId)).ToArray(),
                AstronomyAgendaCityId = next.HomeCityId,
                WelcomeCompleted = true
            };
        }
        if (next.AstronomyAgendaCityId.Length > 0 && !WorldClockSelection.NormalizePersisted(next.WorldClockCityIds).Contains(next.AstronomyAgendaCityId))
            return OperationResult<AppSettings>.Failure("settings.validation.failed", "SettingsValidationFailed",
                new ValidationIssue("astronomy.agenda.city_id", "city_not_selected", "SettingsFieldInvalid"));
        PersistSettings(next);
        await Task.CompletedTask;
        return OperationResult<AppSettings>.Success("settings.saved", "SettingsSaved", next);
    }, cancellationToken);

    private void PersistSettings(AppSettings settings)
    {
        // Publish only after the atomic settings replacement has completed.
        _store.SaveSettings(settings);
        _settingsSnapshot.Replace(settings);
    }

    private async Task<OperationResult<T>> MutateAsync<T>(Func<Task<OperationResult<T>>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await operation().ConfigureAwait(false);
        }
        finally { _mutations.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Drain the active settings write before releasing widget services. Queued callers fail after acquiring the gate.
        await _mutations.WaitAsync().ConfigureAwait(false);
        try { _worldClockOperations.Dispose(); }
        finally { _mutations.Release(); }
    }

    /// <inheritdoc />
    public IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure) =>
        _windowSnapping.Register(windowHandle, reportFailure);

    /// <inheritdoc />
    public void ConfigureWindowSnapping(bool enabled) => _windowSnapping.Configure(enabled);

    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = _applicationLogs.OpenLatestLog();
            _logger.LogInformation("Application log opened through the diagnostics facade.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.opened", "ApplicationLogOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Application log could not be opened. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = _applicationLogs.OpenLogDirectory();
            _logger.LogInformation("Application log directory opened through the diagnostics facade.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.folder.opened", "ApplicationLogFolderOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Application log directory could not be opened. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (windowHandle == 0)
        {
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.share.window.invalid", "ApplicationLogShareWindowInvalid"));
        }

        try
        {
            _ = _applicationLogs.ShareLatestRedactedLog(new IntPtr(windowHandle));
            _logger.LogInformation("Redacted application log prepared for sharing.");
            return Task.FromResult(OperationResult<bool>.Success("diagnostics.log.share.opened", "ApplicationLogShareOpened", true));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException or
                NotSupportedException or
                System.Runtime.InteropServices.COMException or
                System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning("Redacted application log could not be shared. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<bool>.Failure("diagnostics.log.unavailable", "ApplicationLogUnavailable"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<AppSettings>.Success("settings.loaded", "SettingsLoaded", _settingsSnapshot.Value));
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _worldClockOperations.GetCatalog();
        if (!result.Succeeded || result.Value is null) return Task.FromResult(result);
        var count = WorldClockSelection.NormalizePersisted(_settingsSnapshot.Value.WorldClockCityIds).Count;
        var suggestion = HomeCitySuggestion.Resolve(result.Value.Cities, TimeZoneInfo.Local);
        return Task.FromResult(result with
        {
            Value = result.Value with
            {
                AddDeniedMessageKey = _featureAccess.DeniedWorldClockCreation(count)?.MessageKey,
                SuggestedCityId = suggestion,
                WindowsTimeZoneName = TimeZoneInfo.Local.DisplayName
            }
        });
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _worldClockOperations.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken) =>
        _worldClockOperations.GetCelestialReferenceAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        // Ephemeris searches run off the presentation dispatcher; failures propagate without fabricated events.
        var snapshot = await Task.Run(() =>
        {
            var catalog = _worldClockOperations.GetCatalog().Value
                ?? throw new InvalidDataException("The celestial city catalog is unavailable.");
            return CelestialService.Build(request, catalog, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
        if (request.IncludeSatellites)
            snapshot = snapshot with
            {
                Satellites = await CelestialSatelliteService.GetPositionsAsync(snapshot, cancellationToken).ConfigureAwait(false)
            };
        var zone = TimeZoneInfo.FindSystemTimeZoneById(snapshot.TimeZoneId);
        var calendar = CelestialCalendarCatalog.Current;
        // Read the bundled calendar directly; widget startup never opens activity history.
        var preferences = _settingsSnapshot.Value;
        var firstDate = DateOnly.FromDateTime(snapshot.LocalTime.DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.InstantUtc.AddDays(2), zone).DateTime);
        var holidays = firstDate <= calendar.CoverageEnd && lastDate >= calendar.CoverageStart
            ? calendar.Holidays.Where(item => item.Date >= firstDate && item.Date <= lastDate).ToArray()
            : [];
        var saints = preferences.AstronomyAgendaShowSaints && firstDate <= calendar.CoverageEnd && lastDate >= calendar.CoverageStart
            ? Enumerable.Range(0, lastDate.DayNumber - firstDate.DayNumber + 1)
                .SelectMany(offset => calendar.Saints.Where(item => item.Month == firstDate.AddDays(offset).Month && item.Day == firstDate.AddDays(offset).Day)).ToArray()
            : [];
        var calendarEvents = CelestialCalendarEventService.Build(snapshot, zone, holidays, saints,
            preferences.AstronomyAgendaCountryCodes ?? [], preferences.AstronomyAgendaShowSaints, cancellationToken);
        var spaceWeather = await CelestialSpaceWeatherService.GetAsync(snapshot.InstantUtc, zone, cancellationToken).ConfigureAwait(false);
        snapshot = snapshot with
        {
            SpaceWeather = spaceWeather.Snapshot,
            Agenda = Array.AsReadOnly(snapshot.Agenda.Concat(calendarEvents).Concat(spaceWeather.Agenda)
                .Select(item => item with
                {
                    IsAtReferenceInstant = item.StartUtc <= snapshot.InstantUtc && item.EndUtc is { } end && snapshot.InstantUtc < end
                })
                .OrderBy(item => item.StartUtc).ThenBy(item => item.Kind).ToArray())
        };
        return OperationResult<CelestialSnapshot>.Success("celestial.loaded", "WorldClocksLoaded", snapshot);
    }

    /// <inheritdoc />
    public async Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken)
    {
        var image = await _celestialMap.RenderAsync(request, cancellationToken).ConfigureAwait(false);
        return OperationResult<CelestialMapImage>.Success("celestial.map.loaded", "WorldClocksLoaded", image);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(
        WorldClockConversionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_worldClockOperations.Convert(request));
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(
        string cityId,
        CancellationToken cancellationToken)
    {
        var normalizedId = _worldClockOperations.NormalizeAndValidateCityId(cityId);
        return MutateAsync(
            () =>
            {
                var selection = WorldClockSelection.NormalizePersisted(_settingsSnapshot.Value.WorldClockCityIds);
                // Recheck under the same mutation lock as persistence; a previously opened picker is not an entitlement.
                if (!selection.Contains(normalizedId) && _featureAccess.DeniedWorldClockCreation(selection.Count) is { } denied)
                    return Task.FromResult(OperationResult<WorldClockSelectionState>.Failure("feature.clock_limit", denied.MessageKey, denied));
                return Task.FromResult(_worldClockOperations.AddValidated(normalizedId));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(
        string cityId,
        CancellationToken cancellationToken) => MutateAsync(
            () => Task.FromResult(_worldClockOperations.Remove(cityId)),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(
        string cityId,
        WorldClockMoveDirection direction,
        CancellationToken cancellationToken)
    {
        var normalizedId = _worldClockOperations.NormalizeAndValidateCityId(cityId);
        return MutateAsync(
            () => Task.FromResult(_worldClockOperations.MoveValidated(normalizedId, direction)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(
        string secret,
        CancellationToken cancellationToken) => MutateAsync(
            () => _worldClockOperations.SetWeatherKeyAsync(secret, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<FeatureAccessSnapshot>.Success("features.loaded", "Premium.Status", _featureAccess.Snapshot));
    }

    /// <inheritdoc />
    public Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var state = _windowState.Restore(windowKey, windowHandle);
        await Task.CompletedTask;
        return OperationResult<WindowState?>.Success(
            state is null ? "window.state.not_found" : "window.state.restored",
            state is null ? "WindowStateNotFound" : "WindowStateRestored",
            state);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var count = new OpenWindowVisibilityService().Reveal(request, cancellationToken);
            return Task.FromResult(OperationResult<int>.Success("window.reveal.completed", "WindowRevealCompleted", count));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A failed reveal is reported without retrying activation or reopening persisted workspace windows.
            _logger.LogWarning("Open windows could not be revealed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return Task.FromResult(OperationResult<int>.Failure("window.reveal.failed", "Window.RevealFailed"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var (state, settings) = _windowState.Save(windowKey, windowHandle);
        // Publish the committed settings before another serialized mutation can overwrite the saved placement.
        _settingsSnapshot.Replace(settings);
        await Task.CompletedTask;
        return OperationResult<WindowState>.Success("window.state.saved", "WindowStateSaved", state);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _windowState.SetOpenState(windowKey, isOpen);
        _settingsSnapshot.Replace(settings);
        await Task.CompletedTask;
        return OperationResult<bool>.Success("window.open_state.saved", "WindowOpenStateSaved", isOpen);
    }, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var enabled = await _startup.IsEnabledAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.Success("startup.status.loaded", "StartupStatusLoaded", enabled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Windows startup status could not be read. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return OperationResult<bool>.Failure("startup.status.unavailable", "StartupStatusUnavailable");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken) => MutateAsync(async () =>
    {
        var settings = _settingsSnapshot.Value;
        bool success;
        try
        {
            success = await _startup.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Windows startup registration could not be updated. Enabled={Enabled} ExceptionType={ExceptionType}",
                enabled,
                exception.GetType().Name);
            return OperationResult<bool>.Failure("startup.unavailable", "StartupUpdateFailed");
        }

        if (!success)
        {
            return OperationResult<bool>.Failure("startup.failed", "StartupUpdateFailed");
        }

        try
        {
            PersistSettings(settings with { StartWithWindows = enabled });
        }
        catch
        {
            if (!await _startup.SetEnabledAsync(settings.StartWithWindows, CancellationToken.None).ConfigureAwait(false))
            {
                _logger.LogError("Startup state rollback failed after settings persistence error.");
            }

            throw;
        }
        await Task.CompletedTask;
        return OperationResult<bool>.Success(enabled ? "startup.enabled" : "startup.disabled", enabled ? "StartupEnabled" : "StartupDisabled", enabled);
    }, cancellationToken);

}
