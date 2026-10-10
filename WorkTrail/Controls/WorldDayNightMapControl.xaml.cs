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


using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using PackagedAsset = Windows.Storage.StorageFile;

namespace WorkTrail.Controls;

/// <summary>Renders a responsive equirectangular day/night projection from Core-owned astronomy data.</summary>
public sealed partial class WorldDayNightMapControl : UserControl
{
    private const int MapPixelWidth = 1440;
    private const int MapPixelHeight = 720;
    private const double CelestialMarkerSize = 40d;
    private const string DayMapAssetUri = "ms-appx:///Assets/WorldClocks/Maps/world-map-day.png";
    private const string NightMapAssetUri = "ms-appx:///Assets/WorldClocks/Maps/world-map-night.png";
    private readonly WriteableBitmap _compositeBitmap = new(MapPixelWidth, MapPixelHeight);
    private LocalizationService _strings = new("system");
    private WorldClockMapProjection? _projection;
    private byte[]? _dayTexture;
    private byte[]? _nightTexture;
    private bool _textureLoadStarted;

    /// <summary>Creates the passive world-map renderer.</summary>
    public WorldDayNightMapControl()
    {
        InitializeComponent();
        CompositeMapImage.Source = _compositeBitmap;
        Loaded += WorldDayNightMapControl_Loaded;
    }

    /// <summary>Applies one complete Core projection and refreshes localized map annotations.</summary>
    internal void Apply(WorldClockMapProjection projection, LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(strings);
        ValidateCoordinate(projection.Sun);
        ValidateCoordinate(projection.Moon);
        if (!double.IsFinite(projection.MoonPhaseAngleDegrees))
        {
            throw new InvalidDataException("The world-map lunar phase must be finite.");
        }

        foreach (var city in projection.Cities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(city.CityId);
            ArgumentException.ThrowIfNullOrWhiteSpace(city.CityName);
            ValidateCoordinate(new WorldClockMapCoordinate(city.Latitude, city.Longitude));
        }

        _projection = projection;
        _strings = strings;
        ApplyLanguage();
        if (_dayTexture is not null && _nightTexture is not null)
        {
            RenderLighting();
        }

        RebuildMarkers();
    }

    /// <summary>Refreshes labels without changing the current astronomical projection.</summary>
    internal void ApplyLanguage(LocalizationService strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        _strings = strings;
        ApplyLanguage();
        if (_projection is not null)
        {
            RebuildMarkers();
        }
    }

    private void ApplyLanguage()
    {
        NightLegendText.Text = T("WorldClock.Map.Night").ToUpper(_strings.Culture);
        DawnLegendText.Text = T("WorldClock.Map.Dawn").ToUpper(_strings.Culture);
        DayLegendText.Text = T("WorldClock.Map.Day").ToUpper(_strings.Culture);
        SunsetLegendText.Text = T("WorldClock.Map.Sunset").ToUpper(_strings.Culture);
        AutomationProperties.SetName(MapRoot, T("WorldClock.Map.Title"));
    }

    private void RenderLighting()
    {
        if (_projection is null || _dayTexture is null || _nightTexture is null)
        {
            return;
        }

        var pixels = new byte[MapPixelWidth * MapPixelHeight * 4];
        for (var y = 0; y < MapPixelHeight; y++)
        {
            var latitude = 90d - (((y + 0.5d) / MapPixelHeight) * 180d);
            for (var x = 0; x < MapPixelWidth; x++)
            {
                var longitude = (((x + 0.5d) / MapPixelWidth) * 360d) - 180d;
                var sample = WorldMapLightingProjection.Sample(
                    latitude,
                    longitude,
                    _projection.Sun.Latitude,
                    _projection.Sun.Longitude);
                var offset = ((y * MapPixelWidth) + x) * 4;
                var (tintRed, tintGreen, tintBlue) = sample.Band == WorldMapLightBand.Dawn
                    ? ((byte)255, (byte)154, (byte)36)
                    : ((byte)255, (byte)73, (byte)53);
                var tintBlend = sample.TwilightBlend * 0.42d;
                pixels[offset] = BlendChannel(
                    _nightTexture[offset],
                    _dayTexture[offset],
                    tintBlue,
                    sample.DayTextureBlend,
                    tintBlend);
                pixels[offset + 1] = BlendChannel(
                    _nightTexture[offset + 1],
                    _dayTexture[offset + 1],
                    tintGreen,
                    sample.DayTextureBlend,
                    tintBlend);
                pixels[offset + 2] = BlendChannel(
                    _nightTexture[offset + 2],
                    _dayTexture[offset + 2],
                    tintRed,
                    sample.DayTextureBlend,
                    tintBlend);
                pixels[offset + 3] = 255;
            }
        }

        using var stream = _compositeBitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        _compositeBitmap.Invalidate();
    }

    private async void WorldDayNightMapControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_textureLoadStarted)
        {
            return;
        }

        _textureLoadStarted = true;
        _dayTexture = await LoadTextureAsync(DayMapAssetUri);
        _nightTexture = await LoadTextureAsync(NightMapAssetUri);
        RenderLighting();
    }

    private static async Task<byte[]> LoadTextureAsync(string assetUri)
    {
        var file = await PackagedAsset.GetFileFromApplicationUriAsync(new Uri(assetUri));
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = MapPixelWidth,
            ScaledHeight = MapPixelHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        var pixels = provider.DetachPixelData();
        if (pixels.Length != MapPixelWidth * MapPixelHeight * 4)
        {
            throw new InvalidDataException($"World-map texture '{assetUri}' decoded to an unexpected size.");
        }

        return pixels;
    }

    private static byte BlendChannel(
        byte nightValue,
        byte dayValue,
        byte tintValue,
        double dayTextureBlend,
        double twilightBlend)
    {
        var textureValue = nightValue + ((dayValue - nightValue) * dayTextureBlend);
        var tintedValue = textureValue + ((tintValue - textureValue) * twilightBlend);
        return (byte)Math.Clamp(Math.Round(tintedValue), byte.MinValue, byte.MaxValue);
    }

    private void RebuildMarkers()
    {
        MarkerCanvas.Children.Clear();
        if (_projection is null)
        {
            return;
        }

        foreach (var city in _projection.Cities)
        {
            AddMarker(
                CreateCityMarker(city),
                city.Latitude,
                city.Longitude,
                12d,
                12d);
        }

        AddMarker(CreateSunMarker(_projection.Sun), _projection.Sun.Latitude, _projection.Sun.Longitude, CelestialMarkerSize, CelestialMarkerSize);
        AddMarker(CreateMoonMarker(_projection), _projection.Moon.Latitude, _projection.Moon.Longitude, CelestialMarkerSize, CelestialMarkerSize);
        PositionMarkers();
    }

    private FrameworkElement CreateCityMarker(WorldClockMapCity city)
    {
        var marker = new Ellipse
        {
            Width = 12d,
            Height = 12d,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 142, 124, 246)),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1.5d,
            Tag = new MarkerPosition(city.Latitude, city.Longitude, 12d, 12d)
        };
        var label = FormatPosition(city.CityName, city.Latitude, city.Longitude);
        AutomationProperties.SetName(marker, label);
        ToolTipService.SetToolTip(marker, label);
        return marker;
    }

    private FrameworkElement CreateSunMarker(WorldClockMapCoordinate position)
    {
        var marker = CreateCelestialMarker(isDaylight: true);
        var label = FormatPosition(T("WorldClock.Map.Sun"), position.Latitude, position.Longitude);
        AutomationProperties.SetName(marker, label);
        ToolTipService.SetToolTip(marker, label);
        return marker;
    }

    private FrameworkElement CreateMoonMarker(WorldClockMapProjection projection)
    {
        var phase = LunarPhaseProjection.Create(projection.MoonPhaseAngleDegrees);
        var marker = CreateCelestialMarker(isDaylight: false, projection.MoonPhaseAngleDegrees);
        var label = string.Join(
            " · ",
            T("WorldClock.Map.Moon"),
            T(phase.LocalizationKey),
            $"{phase.IlluminatedPercentage.ToString("0", _strings.Culture)}%",
            FormatCoordinates(projection.Moon.Latitude, projection.Moon.Longitude));
        AutomationProperties.SetName(marker, label);
        ToolTipService.SetToolTip(marker, label);
        return marker;
    }

    private static Grid CreateCelestialMarker(bool isDaylight, double moonPhaseAngleDegrees = 0d)
    {
        var marker = new Grid
        {
            Width = CelestialMarkerSize,
            Height = CelestialMarkerSize,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        marker.Children.Add(new CelestialPhaseControl
        {
            IsDaylight = isDaylight,
            MoonPhaseAngleDegrees = moonPhaseAngleDegrees,
            IsHitTestVisible = false
        });
        return marker;
    }

    private void AddMarker(FrameworkElement marker, double latitude, double longitude, double width, double height)
    {
        marker.Tag = new MarkerPosition(latitude, longitude, width, height);
        MarkerCanvas.Children.Add(marker);
    }

    private void PositionMarkers()
    {
        var viewportWidth = MapRoot.ActualWidth;
        var viewportHeight = MapRoot.ActualHeight;
        if (viewportWidth <= 0d || viewportHeight <= 0d)
        {
            return;
        }

        // Match both images' centered Uniform stretch, including the empty margins.
        // Marker sizes stay in DIPs while their positions follow the fitted 2:1 map.
        var scale = Math.Min(viewportWidth / MapPixelWidth, viewportHeight / MapPixelHeight);
        var width = MapPixelWidth * scale;
        var height = MapPixelHeight * scale;
        var left = (viewportWidth - width) / 2d;
        var top = (viewportHeight - height) / 2d;
        MarkerCanvas.Clip = new RectangleGeometry { Rect = new Rect(left, top, width, height) };
        foreach (var element in MarkerCanvas.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is not MarkerPosition marker)
            {
                throw new InvalidDataException("A world-map marker is missing its geographic position.");
            }

            var point = WorldMapLightingProjection.Project(marker.Latitude, marker.Longitude);
            Canvas.SetLeft(element, left + (point.X * width) - (marker.Width / 2d));
            Canvas.SetTop(element, top + (point.Y * height) - (marker.Height / 2d));
        }
    }

    private void MapRoot_SizeChanged(object sender, SizeChangedEventArgs e) => PositionMarkers();

    private string FormatPosition(string name, double latitude, double longitude) =>
        $"{name} · {FormatCoordinates(latitude, longitude)}";

    private string FormatCoordinates(double latitude, double longitude) =>
        $"{latitude.ToString("+0.0;-0.0;0.0", _strings.Culture)}°, {longitude.ToString("+0.0;-0.0;0.0", _strings.Culture)}°";

    private string T(string key) => _strings.Translate(key);

    private static void ValidateCoordinate(WorldClockMapCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        _ = WorldMapLightingProjection.Project(coordinate.Latitude, coordinate.Longitude);
    }

    private sealed record MarkerPosition(double Latitude, double Longitude, double Width, double Height);
}
