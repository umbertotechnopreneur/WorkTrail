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


using SkiaSharp;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Renders geographic Earth projections with locally calculated solar illumination.</summary>
public sealed class CelestialMapService
{
    private const double Radians = Math.PI / 180;
    private readonly Lazy<TexturePair> _textures;

    /// <summary>Uses the packaged Earth textures. Missing or invalid assets fail the request explicitly.</summary>
    public CelestialMapService()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Assets", "WorldClocks", "Maps", "world-map-day.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "WorldClocks", "Maps", "world-map-night.png"))
    {
    }

    /// <summary>Uses explicit Earth texture paths; reads and decodes each asset once on first rendering.</summary>
    public CelestialMapService(string dayTexturePath, string nightTexturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dayTexturePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(nightTexturePath);
        _textures = new Lazy<TexturePair>(() => new TexturePair(LoadTexture(dayTexturePath), LoadTexture(nightTexturePath)));
    }

    internal CelestialMapService(int width, int height, SKColor[] dayPixels, SKColor[] nightPixels)
    {
        if (width < 1 || height < 1 || dayPixels.Length != width * height || nightPixels.Length != width * height)
        {
            throw new ArgumentException("Texture dimensions and pixel counts must match.");
        }

        _textures = new Lazy<TexturePair>(() => new TexturePair(
            new Texture(width, height, (SKColor[])dayPixels.Clone()),
            new Texture(width, height, (SKColor[])nightPixels.Clone())));
    }

    /// <summary>Renders off the presentation thread and honors cancellation between scanlines.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The projection, coordinate, or resolution is unsupported.</exception>
    public Task<CelestialMapImage> RenderAsync(CelestialMapRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Projection)
            || request.UtcNow.UtcDateTime.Year is < 1900 or > 2100
            || request.PixelWidth is < 64 or > 2048 || request.PixelHeight is < 64 or > 2048
            || !double.IsFinite(request.CenterLatitude) || Math.Abs(request.CenterLatitude) > 90
            || !double.IsFinite(request.CenterLongitude) || Math.Abs(request.CenterLongitude) > 180)
        {
            // Reject invalid rendering input before texture I/O or a large pixel allocation.
            throw new ArgumentOutOfRangeException(nameof(request), "Earth projection, dimensions, or coordinates are invalid.");
        }

        return Task.Run(() => Render(request, cancellationToken), cancellationToken);
    }

    private CelestialMapImage Render(CelestialMapRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var textures = _textures.Value;
        var sun = LocalAstronomy.CalculateGlobal(request.UtcNow);
        var sunLatitude = sun.SunLatitude * Radians;
        var sunLongitude = sun.SunLongitude * Radians;
        var sinSun = Math.Sin(sunLatitude);
        var cosSun = Math.Cos(sunLatitude);
        var twilightSine = Math.Sin(6 * Radians);
        var centerLatitude = request.CenterLatitude * Radians;
        var centerLongitude = request.CenterLongitude * Radians;
        var sinCenter = Math.Sin(centerLatitude);
        var cosCenter = Math.Cos(centerLatitude);
        var pixels = new SKColor[request.PixelWidth * request.PixelHeight];
        var radius = Math.Min(request.PixelWidth, request.PixelHeight) * 0.465;

        for (var y = 0; y < request.PixelHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < request.PixelWidth; x++)
            {
                double latitude;
                double longitude;
                var limb = 1d;
                if (request.Projection == CelestialMapProjection.Globe)
                {
                    var east = (x + 0.5 - request.PixelWidth / 2d) / radius;
                    var north = (request.PixelHeight / 2d - y - 0.5) / radius;
                    var distanceSquared = east * east + north * north;
                    if (distanceSquared > 1)
                    {
                        // A narrow translucent atmosphere leaves the surrounding acrylic visible.
                        var halo = Math.Clamp((1.055 - Math.Sqrt(distanceSquared)) / 0.055, 0, 1);
                        pixels[y * request.PixelWidth + x] = new SKColor(64, 133, 214, (byte)(halo * halo * 95));
                        continue;
                    }

                    var forward = Math.Sqrt(1 - distanceSquared);
                    latitude = Math.Asin(Math.Clamp(north * cosCenter + forward * sinCenter, -1, 1));
                    longitude = centerLongitude + Math.Atan2(east, forward * cosCenter - north * sinCenter);
                    limb = 0.72 + 0.28 * Math.Sqrt(forward);
                }
                else
                {
                    latitude = (0.5 - (y + 0.5) / request.PixelHeight) * Math.PI;
                    longitude = centerLongitude + ((x + 0.5) / request.PixelWidth - 0.5) * 2 * Math.PI;
                }

                var solarDot = Math.Sin(latitude) * sinSun
                    + Math.Cos(latitude) * cosSun * Math.Cos(longitude - sunLongitude);
                // Civil twilight provides a continuous transition at the real solar terminator.
                var daylight = Math.Clamp((solarDot + twilightSine) / (2 * twilightSine), 0, 1);
                daylight = daylight * daylight * (3 - 2 * daylight);
                var day = Sample(textures.Day, latitude, longitude);
                var night = Sample(textures.Night, latitude, longitude);
                var dayExposure = 0.72 + 0.28 * Math.Sqrt(Math.Max(0, solarDot));
                pixels[y * request.PixelWidth + x] = new SKColor(
                    Channel(day.Red, night.Red, daylight, dayExposure, limb),
                    Channel(day.Green, night.Green, daylight, dayExposure, limb),
                    Channel(day.Blue, night.Blue, daylight, dayExposure, limb), 255);
            }
        }

        // Dispose all native image buffers after encoding; only immutable texture arrays are retained.
        using var bitmap = new SKBitmap(new SKImageInfo(request.PixelWidth, request.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The Earth projection could not be encoded.");
        if (encoded.Size > 10 * 1024 * 1024)
        {
            // Leave room for Base64 expansion and the envelope in the shared 16 MiB runtime protocol.
            throw new InvalidDataException("The Earth projection exceeds the supported image payload size.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CelestialMapImage(encoded.ToArray(), request.PixelWidth, request.PixelHeight,
            request.UtcNow.ToUniversalTime(), request.Projection, sun.SunLatitude, sun.SunLongitude);
    }

    private static byte Channel(byte day, byte night, double daylight, double exposure, double limb) =>
        (byte)Math.Clamp(Math.Round((day * exposure * daylight + night * (1 - daylight)) * limb), 0, 255);

    private static SKColor Sample(Texture texture, double latitude, double longitude)
    {
        var u = longitude / (2 * Math.PI) + 0.5;
        u -= Math.Floor(u);
        var px = u * texture.Width - 0.5;
        var py = Math.Clamp((0.5 - latitude / Math.PI) * texture.Height - 0.5, 0, texture.Height - 1);
        var x0 = (int)Math.Floor(px);
        var y0 = (int)Math.Floor(py);
        var fx = px - x0;
        var fy = py - y0;
        var left = (x0 + texture.Width) % texture.Width;
        var right = (left + 1) % texture.Width;
        var bottom = Math.Min(y0 + 1, texture.Height - 1);
        var a = texture.Pixels[y0 * texture.Width + left];
        var b = texture.Pixels[y0 * texture.Width + right];
        var c = texture.Pixels[bottom * texture.Width + left];
        var d = texture.Pixels[bottom * texture.Width + right];
        return new SKColor(Blend(a.Red, b.Red, c.Red, d.Red, fx, fy),
            Blend(a.Green, b.Green, c.Green, d.Green, fx, fy),
            Blend(a.Blue, b.Blue, c.Blue, d.Blue, fx, fy));
    }

    private static byte Blend(byte a, byte b, byte c, byte d, double fx, double fy) =>
        (byte)Math.Round((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy);

    private static Texture LoadTexture(string path)
    {
        // Core owns packaged-asset I/O; missing files and decode failures are not hidden by a substitute image.
        using var stream = File.OpenRead(path);
        using var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException($"The Earth texture is not a valid image: {Path.GetFileName(path)}.");
        if (bitmap.Width < 2 || bitmap.Height < 2 || bitmap.Width != bitmap.Height * 2)
        {
            throw new InvalidDataException("Earth textures must use a 2:1 equirectangular projection.");
        }

        return new Texture(bitmap.Width, bitmap.Height, bitmap.Pixels);
    }

    private sealed record Texture(int Width, int Height, SKColor[] Pixels);
    private sealed record TexturePair(Texture Day, Texture Night);
}
