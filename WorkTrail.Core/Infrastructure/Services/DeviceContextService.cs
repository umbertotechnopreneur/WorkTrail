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
using System.Runtime.InteropServices;
using Windows.Devices.Geolocation;

namespace WorkTrail.Services;

/// <summary>
/// Represents one device-context value together with the OS source that supplied it.
/// </summary>
/// <param name="Value">The captured value, or <see langword="null"/> when it is unavailable.</param>
/// <param name="Source">The local Windows or .NET source used to obtain the value.</param>
/// <param name="Status">A stable availability status for the captured value.</param>
public sealed record DeviceContextValue(string? Value, string Source, string Status);

/// <summary>
/// Represents a device location obtained exclusively from the Windows location service.
/// </summary>
/// <param name="Latitude">Latitude in WGS84 degrees, or <see langword="null"/> when unavailable.</param>
/// <param name="Longitude">Longitude in WGS84 degrees, or <see langword="null"/> when unavailable.</param>
/// <param name="AccuracyMeters">Windows-reported horizontal accuracy in meters, when available.</param>
/// <param name="Source">The local provider that supplied the location.</param>
/// <param name="Status">A stable availability or permission status.</param>
public sealed record DeviceLocationSnapshot(
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    string Source,
    string Status);

/// <summary>
/// Represents the result of an explicit foreground request for Windows location consent.
/// </summary>
/// <param name="Source">The Windows API used to request access.</param>
/// <param name="Status">The granted, denied, unspecified, or unavailable status.</param>
public sealed record DeviceLocationAccessResult(string Source, string Status);

/// <summary>
/// Contains local device context that can be attached to a later analysis snapshot.
/// </summary>
/// <param name="TimeZone">The current Windows time-zone identifier.</param>
/// <param name="WindowsUiLanguage">The Windows display/UI language for the signed-in user.</param>
/// <param name="InputLanguage">The keyboard language active for the foreground window when available.</param>
/// <param name="Location">The Windows location result and its provenance.</param>
public sealed record DeviceContextSnapshot(
    DeviceContextValue TimeZone,
    DeviceContextValue WindowsUiLanguage,
    DeviceContextValue InputLanguage,
    DeviceLocationSnapshot Location);

/// <summary>
/// Abstracts Windows device APIs so device-context policy can be tested without querying the host OS.
/// </summary>
public interface IDeviceContextPlatform
{
    /// <summary>Reads the current device time-zone value.</summary>
    DeviceContextValue GetTimeZone();

    /// <summary>Reads the signed-in user's Windows display/UI language.</summary>
    DeviceContextValue GetWindowsUiLanguage();

    /// <summary>Reads the input language associated with the foreground window when possible.</summary>
    DeviceContextValue GetActiveInputLanguage();

    /// <summary>
    /// Reads a location only when Windows has already granted access; implementations must not prompt here.
    /// </summary>
    Task<DeviceLocationSnapshot> GetCurrentLocationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Requests location access from Windows. Callers must invoke this only from a foreground UI thread.
    /// </summary>
    Task<DeviceLocationAccessResult> RequestLocationAccessAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Captures local device context without performing IP lookups, geocoding, or application-managed network calls.
/// </summary>
public sealed class DeviceContextService
{
    private const string FallbackSource = "windows-device-context";
    private readonly IDeviceContextPlatform _platform;

    /// <summary>
    /// Creates a collector backed by the Windows time-zone, language, input-layout, and geolocation APIs.
    /// </summary>
    public DeviceContextService()
        : this(new WindowsDeviceContextPlatform())
    {
    }

    /// <summary>
    /// Creates a collector with a supplied platform implementation, primarily for deterministic tests.
    /// </summary>
    /// <param name="platform">The local platform adapter used to obtain device values.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="platform"/> is <see langword="null"/>.</exception>
    public DeviceContextService(IDeviceContextPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    /// <summary>
    /// Captures the current local context. It never displays a location-permission prompt.
    /// </summary>
    /// <param name="cancellationToken">Cancels the capture before or after the bounded Windows location request.</param>
    /// <returns>A snapshot with local values and a location status; unavailable values are represented explicitly.</returns>
    public async Task<DeviceContextSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        => await CaptureAsync(includeLocation: true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Captures the current local context, optionally omitting precise location even when Windows has granted access.
    /// </summary>
    /// <param name="includeLocation">Whether latitude and longitude may be read from the Windows location service.</param>
    /// <param name="cancellationToken">Cancels the capture before or after the bounded Windows location request.</param>
    /// <returns>A snapshot with local values and an explicit location status.</returns>
    public async Task<DeviceContextSnapshot> CaptureAsync(bool includeLocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeZone = ReadValue(_platform.GetTimeZone);
        var windowsUiLanguage = ReadValue(_platform.GetWindowsUiLanguage);
        var inputLanguage = ReadValue(_platform.GetActiveInputLanguage);
        var location = includeLocation
            ? await ReadLocationAsync(cancellationToken).ConfigureAwait(false)
            : new DeviceLocationSnapshot(null, null, null, "windows-geolocator", "disabled_by_setting");

        return new DeviceContextSnapshot(timeZone, windowsUiLanguage, inputLanguage, location);
    }

    /// <summary>
    /// Requests Windows location access for this app. This operation must be initiated by foreground UI code.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request before it begins or after Windows returns a result.</param>
    /// <returns>The Windows consent result without coordinates.</returns>
    public async Task<DeviceLocationAccessResult> RequestLocationAccessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _platform.RequestLocationAccessAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Consent failures must not destabilize tracking; UI can direct the user to Windows privacy settings.
            return new DeviceLocationAccessResult(FallbackSource, "unavailable");
        }
    }

    private DeviceContextValue ReadValue(Func<DeviceContextValue> reader)
    {
        try
        {
            return reader();
        }
        catch
        {
            // OS interop can fail in non-interactive sessions; keep the snapshot usable with an explicit fallback.
            return new DeviceContextValue(null, FallbackSource, "unavailable");
        }
    }

    private async Task<DeviceLocationSnapshot> ReadLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var location = await _platform.GetCurrentLocationAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return NormalizeLocation(location);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Never fall back to IP or web geolocation: absence is safer and preserves the privacy boundary.
            return new DeviceLocationSnapshot(null, null, null, FallbackSource, "unavailable");
        }
    }

    private static DeviceLocationSnapshot NormalizeLocation(DeviceLocationSnapshot location)
    {
        var hasLatitude = location.Latitude.HasValue;
        var hasLongitude = location.Longitude.HasValue;
        if (!hasLatitude && !hasLongitude)
        {
            return location with { AccuracyMeters = NormalizeAccuracy(location.AccuracyMeters) };
        }

        if (!hasLatitude || !hasLongitude ||
            location.Latitude is < -90 or > 90 ||
            location.Longitude is < -180 or > 180)
        {
            return new DeviceLocationSnapshot(null, null, null, location.Source, "invalid_coordinates");
        }

        return location with { AccuracyMeters = NormalizeAccuracy(location.AccuracyMeters) };
    }

    private static double? NormalizeAccuracy(double? accuracyMeters) => accuracyMeters is >= 0 ? accuracyMeters : null;
}

internal sealed class WindowsDeviceContextPlatform : IDeviceContextPlatform
{
    private const string TimeZoneSource = "windows-time-zone";
    private const string UiLanguageSource = "windows-user-ui-language";
    private const string InputLanguageSource = "windows-foreground-keyboard-layout";
    private const string LocationSource = "windows-geolocator";

    public DeviceContextValue GetTimeZone()
    {
        try
        {
            var timeZoneId = TimeZoneInfo.Local.Id;
            return string.IsNullOrWhiteSpace(timeZoneId)
                ? new DeviceContextValue(null, TimeZoneSource, "unavailable")
                : new DeviceContextValue(timeZoneId, TimeZoneSource, "available");
        }
        catch
        {
            return new DeviceContextValue(null, TimeZoneSource, "unavailable");
        }
    }

    public DeviceContextValue GetWindowsUiLanguage()
    {
        try
        {
            var language = ToCultureName(GetUserDefaultUILanguage());
            if (!string.IsNullOrWhiteSpace(language))
            {
                return new DeviceContextValue(language, UiLanguageSource, "available");
            }

            var fallback = CultureInfo.InstalledUICulture.Name;
            return string.IsNullOrWhiteSpace(fallback)
                ? new DeviceContextValue(null, UiLanguageSource, "unavailable")
                : new DeviceContextValue(fallback, "dotnet-installed-ui-culture", "fallback");
        }
        catch
        {
            return new DeviceContextValue(null, UiLanguageSource, "unavailable");
        }
    }

    public DeviceContextValue GetActiveInputLanguage()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            var source = InputLanguageSource;
            var layout = IntPtr.Zero;
            if (foregroundWindow != IntPtr.Zero)
            {
                var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
                if (threadId != 0)
                {
                    layout = GetKeyboardLayout(threadId);
                }
            }

            if (layout == IntPtr.Zero)
            {
                source = "windows-current-thread-keyboard-layout";
                layout = GetKeyboardLayout(0);
            }

            var languageId = (ushort)((ulong)layout.ToInt64() & 0xffff);
            var language = ToCultureName(languageId);
            return string.IsNullOrWhiteSpace(language)
                ? new DeviceContextValue(null, source, "unavailable")
                : new DeviceContextValue(language, source, "available");
        }
        catch
        {
            return new DeviceContextValue(null, InputLanguageSource, "unavailable");
        }
    }

    public async Task<DeviceLocationSnapshot> GetCurrentLocationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var locator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.Default
            };

            // This uses only the Windows location service and its bounded cached/current-position operation.
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(8));
            cancellationToken.ThrowIfCancellationRequested();

            var coordinates = position.Coordinate.Point.Position;
            return new DeviceLocationSnapshot(
                coordinates.Latitude,
                coordinates.Longitude,
                position.Coordinate.Accuracy,
                LocationSource,
                "available");
        }
        catch (UnauthorizedAccessException)
        {
            return new DeviceLocationSnapshot(null, null, null, LocationSource, "permission_not_granted");
        }
        catch (TaskCanceledException)
        {
            return new DeviceLocationSnapshot(null, null, null, LocationSource, "timed_out");
        }
        catch
        {
            return new DeviceLocationSnapshot(null, null, null, LocationSource, "unavailable");
        }
    }

    public async Task<DeviceLocationAccessResult> RequestLocationAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var access = await Geolocator.RequestAccessAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var status = access switch
            {
                GeolocationAccessStatus.Allowed => "allowed",
                GeolocationAccessStatus.Denied => "denied",
                GeolocationAccessStatus.Unspecified => "unspecified",
                _ => "unavailable"
            };
            return new DeviceLocationAccessResult(LocationSource, status);
        }
        catch (UnauthorizedAccessException)
        {
            return new DeviceLocationAccessResult(LocationSource, "denied");
        }
        catch
        {
            // RequestAccessAsync requires a foreground UI thread; callers get a safe status instead of a thrown WinRT error.
            return new DeviceLocationAccessResult(LocationSource, "foreground_ui_required_or_unavailable");
        }
    }

    private static string? ToCultureName(ushort languageId)
    {
        if (languageId == 0)
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(languageId).Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
