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


using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class CelestialRuntimeTests
{
    /// <summary>Preserves Earth viewpoint, requested instant, binary image bytes and explicit failures across the shared pipe.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarthProjection_RoundTripsThroughTheExistingRuntime(bool fail)
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, CelestialRuntimeProxy>();
        var proxy = (CelestialRuntimeProxy)application;
        proxy.Fail = fail;
        var installation = $"celestial-map-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.FromSeconds(5));
        var request = new CelestialMapRequest(
            new DateTimeOffset(2026, 9, 19, 12, 34, 56, TimeSpan.Zero),
            CelestialMapProjection.Globe, 800, 600, -33.87, 151.21);

        var result = await client.GetCelestialMapAsync(request, CancellationToken.None);

        Assert.Equal(request, proxy.MapRequest);
        Assert.Equal(!fail, result.Succeeded);
        Assert.Equal(fail ? "celestial.map.failed" : "celestial.map.loaded", result.Code);
        if (!fail)
        {
            Assert.Equal(new byte[] { 137, 80, 78, 71, 0, 255 }, result.Value!.PngBytes);
            Assert.Equal(request.UtcNow, result.Value.UtcNow);
            Assert.Equal(request.Projection, result.Value.Projection);
            Assert.Equal(800, result.Value.PixelWidth);
            Assert.Equal(600, result.Value.PixelHeight);
            Assert.Equal(-2.25, result.Value.SunLatitude);
            Assert.Equal(158.75, result.Value.SunLongitude);
        }
    }

    /// <summary>Preserves city-local offsets, nullable event intervals and star magnitudes through the celestial wire contract.</summary>
    [Fact]
    public async Task Ephemeris_RoundTripsLocalOffsetsAndNullableValues()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, CelestialRuntimeProxy>();
        var proxy = (CelestialRuntimeProxy)application;
        var installation = $"celestial-sky-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.FromSeconds(5));
        var request = new CelestialRequest("ho-chi-minh-city", new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));

        var result = await client.GetCelestialAsync(request, CancellationToken.None);

        Assert.Equal(request, proxy.SkyRequest);
        Assert.True(result.Succeeded);
        Assert.Equal(TimeSpan.FromHours(7), result.Value!.LocalTime.Offset);
        Assert.Equal(CelestialBodyKind.Moon, Assert.Single(result.Value.Bodies).Kind);
        Assert.Null(Assert.Single(result.Value.Stars).Magnitude);
        var agendaEvent = Assert.Single(result.Value.Agenda);
        Assert.Equal(CelestialEventKind.NewMoon, agendaEvent.Kind);
        Assert.Equal(TimeSpan.FromHours(7), agendaEvent.StartLocal.Offset);
        Assert.Null(agendaEvent.EndUtc);
        Assert.Null(agendaEvent.EndLocal);
        Assert.Equal(TropicalZodiacSign.Virgo, result.Value.Zodiac.CurrentSign);
        Assert.Equal(12, result.Value.Zodiac.Signs.Count);
        Assert.Equal(CelestialSkyPalette.Create(-30), result.Value.SkyAppearance);
    }

    public class CelestialRuntimeProxy : DispatchProxy
    {
        internal bool Fail { get; set; }
        internal CelestialMapRequest? MapRequest { get; private set; }
        internal CelestialRequest? SkyRequest { get; private set; }

        /// <summary>Provides inert typed astronomy results without opening assets or creating a second tracking runtime.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IWorkTrailApplication.GetCelestialMapAsync))
            {
                MapRequest = (CelestialMapRequest)args![0]!;
                return Task.FromResult(Fail
                    ? OperationResult<CelestialMapImage>.Failure("celestial.map.failed", "Celestial.Unavailable")
                    : OperationResult<CelestialMapImage>.Success("celestial.map.loaded", "WorldClocksLoaded",
                        new CelestialMapImage([137, 80, 78, 71, 0, 255], MapRequest.PixelWidth, MapRequest.PixelHeight,
                            MapRequest.UtcNow, MapRequest.Projection, -2.25, 158.75)));
            }

            if (targetMethod?.Name == nameof(IWorkTrailApplication.GetCelestialAsync))
            {
                SkyRequest = (CelestialRequest)args![0]!;
                var instant = SkyRequest.InstantUtc;
                var local = instant.ToOffset(TimeSpan.FromHours(7));
                return Task.FromResult(OperationResult<CelestialSnapshot>.Success("celestial.loaded", "WorldClocksLoaded",
                    new CelestialSnapshot(SkyRequest.CityId, "Ho Chi Minh City", "SE Asia Standard Time",
                        instant, local, 10.82, 106.63, 0, -30,
                        [new(CelestialBodyKind.Moon, 15, 90, true)], [new("star", "Star", 30, 45, null)], [],
                        [new(CelestialEventKind.NewMoon, instant, null, local, null)])
                    {
                        SkyAppearance = CelestialSkyPalette.Create(-30),
                        Zodiac = new CelestialZodiacSnapshot(TropicalZodiacSign.Virgo, 176,
                            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Enum.GetValues<TropicalZodiacSign>(),
                                sign => new CelestialZodiacSector(sign, (int)sign * 30, ((int)sign + 1) * 30))))
                    }));
            }

            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync)) return ValueTask.CompletedTask;
            if (targetMethod?.Name is "add_RuntimeStateChanged" or "remove_RuntimeStateChanged") return null;
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
