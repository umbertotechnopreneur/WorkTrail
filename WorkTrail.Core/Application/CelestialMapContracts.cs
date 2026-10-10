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


namespace WorkTrail.Application;

/// <summary>Selects the geographic projection for the independent Earth window.</summary>
public enum CelestialMapProjection
{
    /// <summary>Displays all longitudes in an equirectangular map.</summary>
    Flat,

    /// <summary>Displays the visible hemisphere as an orthographic globe.</summary>
    Globe
}

/// <summary>Requests an Earth image between 1900 and 2100 inclusive; coordinates are degrees, east-positive, and dimensions are pixels.</summary>
public sealed record CelestialMapRequest(
    DateTimeOffset UtcNow,
    CelestialMapProjection Projection,
    int PixelWidth = 960,
    int PixelHeight = 640,
    double CenterLatitude = 20,
    double CenterLongitude = 15);

/// <summary>Contains an application-rendered PNG and the actual subsolar point for its UTC instant.</summary>
public sealed record CelestialMapImage(
    byte[] PngBytes,
    int PixelWidth,
    int PixelHeight,
    DateTimeOffset UtcNow,
    CelestialMapProjection Projection,
    double SunLatitude,
    double SunLongitude);
