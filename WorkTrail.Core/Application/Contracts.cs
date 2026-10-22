// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;


/// <summary>Describes a validation error without coupling callers to a presentation technology.</summary>
public sealed record ValidationIssue(string Field, string Code, string MessageKey, long? ActualLength = null, long? Limit = null);


/// <summary>Provides the stable result contract returned by every mutating application operation.</summary>
public sealed record OperationResult<T>(
    bool Succeeded,
    string Code,
    string MessageKey,
    T? Value,
    IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Creates a successful result.</summary>
    public static OperationResult<T> Success(string code, string messageKey, T? value = default) => new(true, code, messageKey, value, Array.Empty<ValidationIssue>());

    /// <summary>Creates a failed result.</summary>
    public static OperationResult<T> Failure(string code, string messageKey, params ValidationIssue[] issues) => new(false, code, messageKey, default, issues);
}


/// <summary>Identifies one persisted top-level window placement in physical pixels.</summary>
public sealed record WindowState(
    int X,
    int Y,
    int Width,
    int Height,
    string MonitorDeviceName)
{
    /// <summary>Gets the effective window DPI; zero identifies a legacy physical-pixel placement.</summary>
    public uint Dpi { get; init; }

    /// <summary>Gets the work area used to retain monitor-relative offsets after topology changes.</summary>
    public WindowWorkArea? MonitorWorkArea { get; init; }

    /// <summary>Gets the monitor device interface identity when Windows exposes one.</summary>
    public string? MonitorId { get; init; }
}



/// <summary>Defines the minimum usable logical dimensions for a window surface.</summary>
public readonly record struct WindowMinimumSize(int Width, int Height);


/// <summary>Describes a monitor work area in physical pixels.</summary>
public readonly record struct WindowWorkArea(int X, int Y, int Width, int Height)
{
    /// <summary>Gets the exclusive right edge of the work area.</summary>
    public int Right => checked(X + Width);

    /// <summary>Gets the exclusive bottom edge of the work area.</summary>
    public int Bottom => checked(Y + Height);
}


/// <summary>Calculates safe window bounds without depending on a windowing API.</summary>
public static class WindowStateCalculator
{
    /// <summary>Retains logical size and monitor-relative position across DPI and display-layout changes.</summary>
    public static WindowState RebaseToMonitor(
        WindowState state, WindowWorkArea workArea, string monitorDeviceName, uint dpi,
        WindowMinimumSize minimumSize)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (dpi == 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (state.Width <= 0 || state.Height <= 0 || minimumSize.Width <= 0 || minimumSize.Height <= 0)
            throw new InvalidOperationException("Persisted window state or minimum size is invalid.");
        var ratio = state.Dpi == 0 ? 1d : (double)dpi / state.Dpi;
        var source = state.MonitorWorkArea;
        var rebased = state with
        {
            X = source is { } oldAreaX ? Round(workArea.X + ((double)state.X - oldAreaX.X) * ratio) : state.X,
            Y = source is { } oldAreaY ? Round(workArea.Y + ((double)state.Y - oldAreaY.Y) * ratio) : state.Y,
            Width = Math.Max(1, Round(state.Width * ratio)),
            Height = Math.Max(1, Round(state.Height * ratio)),
            Dpi = dpi,
            MonitorWorkArea = workArea
        };
        return ClampToWorkArea(rebased, workArea, monitorDeviceName,
            Math.Max(1, Round(Math.Ceiling(minimumSize.Width * dpi / 96d))),
            Math.Max(1, Round(Math.Ceiling(minimumSize.Height * dpi / 96d))));
    }

    private static int Round(double value) => (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);

    /// <summary>Clamps a saved placement to the supplied monitor work area and minimum usable size.</summary>
    public static WindowState ClampToWorkArea(
        WindowState state,
        WindowWorkArea workArea,
        string monitorDeviceName,
        int minimumWidth = 1,
        int minimumHeight = 1)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "A monitor work area must have positive dimensions.");
        }

        if (state.Width <= 0 ||
            state.Height <= 0 ||
            minimumWidth <= 0 ||
            minimumHeight <= 0 ||
            string.IsNullOrWhiteSpace(state.MonitorDeviceName))
        {
            throw new InvalidOperationException("Persisted window state is invalid.");
        }

        var boundedMinimumWidth = Math.Min(minimumWidth, workArea.Width);
        var boundedMinimumHeight = Math.Min(minimumHeight, workArea.Height);
        var width = Math.Clamp(state.Width, boundedMinimumWidth, workArea.Width);
        var height = Math.Clamp(state.Height, boundedMinimumHeight, workArea.Height);
        var x = Math.Clamp(state.X, workArea.X, workArea.Right - width);
        var y = Math.Clamp(state.Y, workArea.Y, workArea.Bottom - height);
        return state with
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MonitorDeviceName = string.IsNullOrWhiteSpace(monitorDeviceName) ? throw new ArgumentException("Monitor device name is required.", nameof(monitorDeviceName)) : monitorDeviceName
        };
    }
}


/// <summary>Contains a whitelist-bound settings patch.</summary>
public sealed record SettingsPatch(IReadOnlyDictionary<string, string?> Values);


/// <summary>Describes non-secret logging and remote-error-reporting diagnostics for the active host.</summary>
public sealed record ObservabilityHealth(
    bool ConsoleLoggingEnabled,
    bool FileLoggingEnabled,
    string SentryStatus,
    bool SendsDefaultPii);


/// <summary>Provides product metadata and safe external links.</summary>
public sealed record BuildInformation(
    int SchemaVersion,
    string SemVer,
    string PackageVersion,
    DateTimeOffset BuiltAtUtc,
    DateTimeOffset BuiltAtLocal,
    string MachineName,
    string GitCommit,
    string GitCommitShort,
    bool GitDirty,
    string Configuration,
    string Platform,
    string RuntimeIdentifier);


/// <summary>Provides product metadata, build provenance and safe external links.</summary>
public sealed record ProductInformation(string Name, string License, string RepositoryUrl, string AuthorUrl, BuildInformation Build);


/// <summary>Describes one city available to the world-clock selector.</summary>
public sealed record WorldClockCitySummary(
    string Id,
    string Name,
    string CountryCode,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    bool IsCapital);


/// <summary>Contains the locally distributed city catalog and current selection limit.</summary>
public sealed record WorldClockCityCatalog(IReadOnlyList<WorldClockCitySummary> Cities, int MaximumClocks, string? AddDeniedMessageKey = null,
    string? SuggestedCityId = null, string? WindowsTimeZoneName = null);


/// <summary>Contains the persisted world-clock identifiers after a successful selection mutation.</summary>
public sealed record WorldClockSelectionState(IReadOnlyList<string> CityIds, int MaximumClocks);


/// <summary>Identifies the adjacent direction used to reorder a selected world clock.</summary>
public enum WorldClockMoveDirection
{
    Unspecified = 0,
    Up = 1,
    Down = 2
}


/// <summary>Describes decorative local-time layers and any source-backed weather layers for one clock.</summary>
public sealed record WorldClockAtmosphere(
    string Phase,
    IReadOnlyList<string> BackdropAssetPaths,
    IReadOnlyList<string> ForegroundAssetPaths);


/// <summary>Contains one fresh source-backed current-weather observation.</summary>
public sealed record WorldClockWeather(
    double TemperatureCelsius,
    string ConditionKey,
    DateTimeOffset ObservedAtUtc,
    bool IsFresh);


/// <summary>Explains whether optional current weather was applied to a world-clock snapshot.</summary>
public sealed record WorldClockWeatherStatus(
    string Provider,
    string State,
    string? ReasonCode,
    int RequestedCities,
    int AvailableObservations,
    bool IsProviderConfigured);


/// <summary>Contains one locally calculated city clock and its celestial events.</summary>
/// <param name="IsDaylightSavingTime">Whether the projected instant is in daylight saving time, independently of sunrise/sunset.</param>
/// <param name="DaylightSavingEndsAt">First instant after active DST ends, using the city's new offset; null when inactive or no future end is defined by installed rules.</param>
public sealed record WorldClockItem(
    string CityId,
    string CityName,
    string CountryCode,
    string TimeZoneId,
    DateTimeOffset LocalTime,
    bool IsDaylightSavingTime,
    DateTimeOffset? DaylightSavingEndsAt,
    bool IsDaylight,
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    double MoonPhaseAngleDegrees,
    string SkylineAssetPath,
    string SkylineSeason,
    WorldClockAtmosphere Atmosphere,
    WorldClockWeather? Weather,
    SpaceWeatherAlert? SpaceWeatherAlert = null);


/// <summary>Identifies one geographic point used by the world day/night projection.</summary>
public sealed record WorldClockMapCoordinate(double Latitude, double Longitude);


/// <summary>Identifies one selected city on the world day/night projection.</summary>
public sealed record WorldClockMapCity(
    string CityId,
    string CityName,
    double Latitude,
    double Longitude);


/// <summary>Describes the home city's offline location and seasonal illustration independently of selected clocks.</summary>
public sealed record WorldClockHomeCity(
    string CityId,
    string CityName,
    string CountryCode,
    double Latitude,
    double Longitude,
    string SkylineAssetPath);


/// <summary>Contains the global celestial and city positions for one shared instant.</summary>
public sealed record WorldClockMapProjection(
    WorldClockMapCoordinate Sun,
    WorldClockMapCoordinate Moon,
    double MoonPhaseAngleDegrees,
    IReadOnlyList<WorldClockMapCity> Cities);


/// <summary>Requests a conversion from one city's local civil time to a shared UTC instant.</summary>
public sealed record WorldClockConversionRequest(string ReferenceCityId, DateTime ReferenceLocalTime);


/// <summary>Contains the complete world-clock projection for one shared instant.</summary>
public sealed record WorldClockSnapshot(
    DateTimeOffset InstantUtc,
    IReadOnlyList<WorldClockItem> Clocks,
    int MaximumClocks,
    WorldClockWeatherStatus WeatherStatus,
    WorldClockMapProjection Map)
{
    /// <summary>Gets the saved home city, or a local suggestion when no home city has been chosen.</summary>
    public WorldClockHomeCity? HomeCity { get; init; }
}

/// <summary>Describes the single widget runtime without activity collection.</summary>
public sealed record RuntimeHealth(string Version, int ProtocolVersion, string InstallationFingerprint,
    bool RuntimeAvailable, IReadOnlyList<string> Capabilities, ObservabilityHealth Observability);

/// <summary>Provides the shared application boundary for desktop widgets and their settings.</summary>
public interface IWorkTrailApplication : IAsyncDisposable
{
    /// <summary>Executes the RegisterWindowSnapping widget operation.</summary>
    IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure);

    /// <summary>Executes the ConfigureWindowSnapping widget operation.</summary>
    void ConfigureWindowSnapping(bool enabled);

    /// <summary>Executes the GetRuntimeHealthAsync widget operation.</summary>
    Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken);

    /// <summary>Executes the GetWorldClocksAsync widget operation.</summary>
    Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken);

    /// <summary>Executes the GetCelestialReferenceAsync widget operation.</summary>
    Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken);

    /// <summary>Executes the GetCelestialAsync widget operation.</summary>
    Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken);

    /// <summary>Executes the GetCelestialMapAsync widget operation.</summary>
    Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken);

    /// <summary>Executes the ConvertWorldClocksAsync widget operation.</summary>
    Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(WorldClockConversionRequest request, CancellationToken cancellationToken);

    /// <summary>Executes the GetWorldClockCityCatalogAsync widget operation.</summary>
    Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Executes the AddWorldClockAsync widget operation.</summary>
    Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(string cityId, CancellationToken cancellationToken);

    /// <summary>Executes the RemoveWorldClockAsync widget operation.</summary>
    Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(string cityId, CancellationToken cancellationToken);

    /// <summary>Executes the MoveWorldClockAsync widget operation.</summary>
    Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(
        string cityId,
        WorldClockMoveDirection direction,
        CancellationToken cancellationToken);

    /// <summary>Executes the SetWorldClockWeatherKeyAsync widget operation.</summary>
    Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(string secret, CancellationToken cancellationToken);

    /// <summary>Executes the OpenApplicationLogAsync widget operation.</summary>
    Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken);

    /// <summary>Executes the OpenApplicationLogFolderAsync widget operation.</summary>
    Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken);

    /// <summary>Executes the ShareApplicationLogAsync widget operation.</summary>
    Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken);

    /// <summary>Executes the GetSettingsAsync widget operation.</summary>
    Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken);

    /// <summary>Executes the GetFeatureAccessAsync widget operation.</summary>
    Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken);

    /// <summary>Executes the PatchSettingsAsync widget operation.</summary>
    Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken);

    /// <summary>Executes the RestoreWindowStateAsync widget operation.</summary>
    Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken);

    /// <summary>Executes the RevealOpenWindowsAsync widget operation.</summary>
    Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken);

    /// <summary>Executes the SaveWindowStateAsync widget operation.</summary>
    Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken);

    /// <summary>Executes the SetWindowOpenStateAsync widget operation.</summary>
    Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken);

    /// <summary>Executes the GetStartupStatusAsync widget operation.</summary>
    Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken);

    /// <summary>Executes the SetStartupEnabledAsync widget operation.</summary>
    Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Executes the GetProductInformationAsync widget operation.</summary>
    Task<OperationResult<ProductInformation>> GetProductInformationAsync(CancellationToken cancellationToken);

    /// <summary>Executes the OpenProductLinkAsync widget operation.</summary>
    Task<OperationResult<bool>> OpenProductLinkAsync(string linkKey, CancellationToken cancellationToken);

}
