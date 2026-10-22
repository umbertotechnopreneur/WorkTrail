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


namespace WorkTrail.Presentation;

/// <summary>Identifies the four visual light zones rendered by the world map.</summary>
public enum WorldMapLightBand
{
    /// <summary>The sun is at or below nautical twilight.</summary>
    Night,

    /// <summary>The sun is approaching or has recently crossed the local horizon.</summary>
    Dawn,

    /// <summary>The sun is high enough for the full daylight texture.</summary>
    Day,

    /// <summary>The sun has recently crossed or is receding below the local horizon.</summary>
    Sunset
}

/// <summary>Contains one normalized equirectangular map coordinate.</summary>
public readonly record struct WorldMapPoint(double X, double Y);

/// <summary>Contains solar altitude plus continuous texture and twilight blend factors.</summary>
public readonly record struct WorldMapLightingSample(
    double SolarAltitudeDegrees,
    WorldMapLightBand Band,
    double DayTextureBlend,
    double TwilightBlend);

/// <summary>Projects geographic and solar coordinates into deterministic world-map presentation data.</summary>
public static class WorldMapLightingProjection
{
    #region Fields

    private const double DegreesToRadians = Math.PI / 180d;
    private const double RadiansToDegrees = 180d / Math.PI;

    #endregion

    #region Implementation

    /// <summary>Projects latitude and longitude into a zero-to-one equirectangular coordinate.</summary>
    public static WorldMapPoint Project(double latitude, double longitude)
    {
        ValidateCoordinate(latitude, longitude);
        var normalizedLongitude = (longitude + 180d) / 360d;
        var normalizedLatitude = (90d - latitude) / 180d;

        return new WorldMapPoint(normalizedLongitude, normalizedLatitude);
    }

    /// <summary>Calculates solar altitude, its display zone, and smooth day/twilight blend factors.</summary>
    public static WorldMapLightingSample Sample(
        double latitude,
        double longitude,
        double subsolarLatitude,
        double subsolarLongitude)
    {
        ValidateCoordinate(latitude, longitude);
        ValidateCoordinate(subsolarLatitude, subsolarLongitude);

        var latitudeRadians = latitude * DegreesToRadians;
        var subsolarLatitudeRadians = subsolarLatitude * DegreesToRadians;
        var hourAngle = NormalizeSignedDegrees(longitude - subsolarLongitude);

        // The spherical dot product gives the sine of the Sun's altitude above this observer's horizon.
        var sineOfAltitude =
            (Math.Sin(latitudeRadians) * Math.Sin(subsolarLatitudeRadians))
            + (Math.Cos(latitudeRadians) * Math.Cos(subsolarLatitudeRadians) * Math.Cos(hourAngle * DegreesToRadians));
        var altitude = Math.Asin(sineOfAltitude) * RadiansToDegrees;

        // Altitude identifies day or night; the signed hour angle distinguishes the two twilight edges.
        var band = altitude >= 6d
            ? WorldMapLightBand.Day
            : altitude <= -12d
                ? WorldMapLightBand.Night
                : hourAngle < 0d ? WorldMapLightBand.Dawn : WorldMapLightBand.Sunset;
        var dayTextureBlend = SmoothStep(-12d, 4d, altitude);
        // Fade twilight in and back out so its overlay does not persist into full day or full night.
        var twilightBlend = SmoothStep(-12d, -2d, altitude)
            * (1d - SmoothStep(-2d, 6d, altitude));

        return new WorldMapLightingSample(altitude, band, dayTextureBlend, twilightBlend);
    }

    /// <summary>Rejects non-finite coordinates and values outside geographic latitude or longitude bounds.</summary>
    private static void ValidateCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }

        if (!double.IsFinite(longitude) || longitude is < -180d or > 180d)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }
    }

    /// <summary>Wraps an angle into the signed half-circle used to distinguish dawn from sunset.</summary>
    private static double NormalizeSignedDegrees(double value)
    {
        var normalized = (value % 360d + 360d) % 360d;

        return normalized > 180d ? normalized - 360d : normalized;
    }

    /// <summary>Interpolates a clamped blend with zero slope at both interval endpoints.</summary>
    private static double SmoothStep(double minimum, double maximum, double value)
    {
        var normalized = Math.Clamp((value - minimum) / (maximum - minimum), 0d, 1d);

        return normalized * normalized * (3d - (2d * normalized));
    }

    #endregion
}
