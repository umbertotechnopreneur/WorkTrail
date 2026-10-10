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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Verifies the isolated application-layer world-clock use cases.</summary>
public sealed class WorldClockApplicationServiceTests
{
    /// <summary>Ensures duplicate and over-capacity additions fail without changing persisted settings.</summary>
    [Fact]
    public void AddValidated_RejectsDuplicatesAndMaximumWithoutPersisting()
    {
        var settings = new SettingsSnapshot(new AppSettings(
            WorldClockCityIds: ["london", "paris", "tokyo", "hanoi", "berlin", "rome", "madrid", "lisbon", "oslo", "stockholm", "helsinki", "vienna"]));
        var persisted = new List<AppSettings>();
        using var service = CreateService(settings, persisted.Add);

        var duplicateId = service.NormalizeAndValidateCityId(" TOKYO ");
        var duplicate = service.AddValidated(duplicateId);
        var pragueId = service.NormalizeAndValidateCityId("prague");
        var maximum = service.AddValidated(pragueId);

        Assert.False(duplicate.Succeeded);
        Assert.Equal("world_clocks.duplicate", duplicate.Code);
        Assert.Equal("cityId", Assert.Single(duplicate.Issues).Field);
        Assert.False(maximum.Succeeded);
        Assert.Equal("world_clocks.maximum_reached", maximum.Code);
        Assert.Equal("cityId", Assert.Single(maximum.Issues).Field);
        Assert.Empty(persisted);
        Assert.Equal(["london", "paris", "tokyo", "hanoi", "berlin", "rome", "madrid", "lisbon", "oslo", "stockholm", "helsinki", "vienna"], settings.Value.WorldClockCityIds);
    }

    /// <summary>Ensures an explicitly empty selection can add its first clock through the normal mutation.</summary>
    [Fact]
    public void AddValidated_FromEmptyPersistsTheFirstClock()
    {
        var settings = new SettingsSnapshot(new AppSettings(WorldClockCityIds: []));
        var persisted = new List<AppSettings>();
        using var service = CreateService(settings, persisted.Add);

        var result = service.AddValidated(service.NormalizeAndValidateCityId(" TOKYO "));

        Assert.True(result.Succeeded);
        Assert.Equal(["tokyo"], result.Value?.CityIds);
        Assert.Equal(["tokyo"], Assert.Single(persisted).WorldClockCityIds);
    }

    /// <summary>Ensures removal reports missing selections and can persist an intentional empty state.</summary>
    [Fact]
    public void Remove_RejectsUnknownAndPersistsAnEmptySelection()
    {
        var settings = new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london", "paris"]));
        var persisted = new List<AppSettings>();
        using var service = CreateService(settings, persisted.Add);

        var missing = service.Remove("tokyo");
        var removed = service.Remove(" PARIS ");
        var final = service.Remove("london");

        Assert.False(missing.Succeeded);
        Assert.Equal("world_clocks.not_found", missing.Code);
        Assert.True(removed.Succeeded);
        Assert.Equal(["london"], removed.Value?.CityIds);
        Assert.True(final.Succeeded);
        Assert.Empty(final.Value?.CityIds ?? []);
        Assert.Equal(2, persisted.Count);
        Assert.Empty(settings.Value.WorldClockCityIds ?? []);
    }

    /// <summary>Ensures adjacent moves persist their exact order and reject unavailable directions.</summary>
    [Fact]
    public void MoveValidated_ReordersAdjacentClocksAndRejectsUnavailableMoves()
    {
        var settings = new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london", "paris", "tokyo"]));
        var persisted = new List<AppSettings>();
        using var service = CreateService(settings, persisted.Add);
        var parisId = service.NormalizeAndValidateCityId(" PARIS ");

        var movedUp = service.MoveValidated(parisId, WorldClockMoveDirection.Up);
        var unavailable = service.MoveValidated(parisId, WorldClockMoveDirection.Up);
        var movedDown = service.MoveValidated(parisId, WorldClockMoveDirection.Down);
        var missing = service.MoveValidated(
            service.NormalizeAndValidateCityId("rome"),
            WorldClockMoveDirection.Up);

        Assert.True(movedUp.Succeeded);
        Assert.Equal(["paris", "london", "tokyo"], movedUp.Value?.CityIds);
        Assert.False(unavailable.Succeeded);
        Assert.Equal("world_clocks.move_unavailable", unavailable.Code);
        Assert.Equal("direction", Assert.Single(unavailable.Issues).Field);
        Assert.True(movedDown.Succeeded);
        Assert.Equal(["london", "paris", "tokyo"], movedDown.Value?.CityIds);
        Assert.False(missing.Succeeded);
        Assert.Equal("world_clocks.not_found", missing.Code);
        Assert.Equal(2, persisted.Count);
        Assert.Equal(["london", "paris", "tokyo"], settings.Value.WorldClockCityIds);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.MoveValidated(parisId, WorldClockMoveDirection.Unspecified));
    }

    /// <summary>Ensures conversion failures identify the request field that needs correction.</summary>
    [Fact]
    public void Convert_MapsReferenceAndCivilTimeFailuresToTheirRequestFields()
    {
        var settings = new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london", "paris"]));
        using var service = CreateService(settings);

        var missingReference = service.Convert(new WorldClockConversionRequest(
            "tokyo",
            new DateTime(2026, 8, 30, 10, 18, 0, DateTimeKind.Unspecified)));
        var invalidCivilTime = service.Convert(new WorldClockConversionRequest(
            "paris",
            new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified)));

        Assert.False(missingReference.Succeeded);
        Assert.Equal("referenceCityId", Assert.Single(missingReference.Issues).Field);
        Assert.False(invalidCivilTime.Succeeded);
        Assert.Equal("world_clocks.local_time.invalid", invalidCivilTime.Code);
        Assert.Equal("referenceLocalTime", Assert.Single(invalidCivilTime.Issues).Field);
    }

    /// <summary>Ensures weather secrets are remotely accepted before the environment contract is written.</summary>
    [Fact]
    public async Task SetWeatherKeyAsync_ValidatesBeforeWritingAndDoesNotEchoTheSecret()
    {
        var settings = new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london"]));
        var writes = new List<(string Name, string Secret)>();
        using var service = CreateService(
            settings,
            setApiKey: (name, secret) => writes.Add((name, secret)));

        var invalid = await service.SetWeatherKeyAsync("short", CancellationToken.None);
        var secret = new string('a', 32);
        var stored = await service.SetWeatherKeyAsync(secret, CancellationToken.None);

        Assert.False(invalid.Succeeded);
        Assert.Equal("world_clocks.weather.key.invalid", invalid.Code);
        var write = Assert.Single(writes);
        Assert.Equal(OpenWeatherCurrentProvider.ApiKeyEnvironmentVariable, write.Name);
        Assert.Equal(secret, write.Secret);
        Assert.True(stored.Succeeded);
        Assert.Equal("world_clocks.weather.key.stored", stored.Code);
        Assert.Equal(OpenWeatherCurrentProvider.ApiKeyEnvironmentVariable, stored.Value);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(stored), StringComparison.Ordinal);
    }

    /// <summary>Ensures rejected or unverifiable keys never reach the Windows environment writer.</summary>
    [Theory]
    [InlineData((int)WorldClockWeatherApiKeyValidation.Rejected, "world_clocks.weather.key.rejected")]
    [InlineData((int)WorldClockWeatherApiKeyValidation.Unavailable, "world_clocks.weather.key.validation_unavailable")]
    public async Task SetWeatherKeyAsync_DoesNotWriteWhenProviderValidationFails(
        int validationValue,
        string expectedCode)
    {
        var validation = (WorldClockWeatherApiKeyValidation)validationValue;
        var writes = new List<(string Name, string Secret)>();
        using var service = CreateService(
            new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london"])),
            setApiKey: (name, secret) => writes.Add((name, secret)),
            validation: validation);

        var result = await service.SetWeatherKeyAsync(new string('b', 32), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Code);
        Assert.Empty(writes);
    }

    /// <summary>A quota response cannot verify a candidate or replace a previously working key.</summary>
    [Fact]
    public async Task SetWeatherKeyAsync_DoesNotStoreAnUnverifiedRateLimitedKey()
    {
        var writes = new List<(string Name, string Secret)>();
        using var service = CreateService(
            new SettingsSnapshot(new AppSettings(WorldClockCityIds: ["london"])),
            setApiKey: (name, secret) => writes.Add((name, secret)),
            validation: WorldClockWeatherApiKeyValidation.RateLimited);

        var result = await service.SetWeatherKeyAsync(new string('c', 32), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("world_clocks.weather.key.rate_limited", result.Code);
        Assert.Empty(writes);
    }

    private static WorldClockApplicationService CreateService(
        SettingsSnapshot settings,
        Action<AppSettings>? onPersist = null,
        Action<string, string>? setApiKey = null,
        WorldClockWeatherApiKeyValidation validation = WorldClockWeatherApiKeyValidation.Accepted)
    {
        var worldClocks = new WorldClockService(
            RepositoryFile("WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3"),
            new ValidationWeatherProvider(validation),
            TimeProvider.System);
        return new WorldClockApplicationService(
            worldClocks,
            settings,
            setApiKey ?? ((_, _) => { }),
            updated =>
            {
                onPersist?.Invoke(updated);
                settings.Replace(updated);
            });
    }

    private sealed class ValidationWeatherProvider(WorldClockWeatherApiKeyValidation validation)
        : IWorldClockWeatherProvider
    {
        public string Name => "validation-test-provider";

        public string ConfigurationState => "configured";

        public bool IsConfigured => true;

        /// <inheritdoc />
        public Task<WorldClockWeatherApiKeyValidation> ValidateApiKeyAsync(
            string secret,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(validation);
        }

        /// <inheritdoc />
        public Task<WorldClockWeatherObservation> GetCurrentAsync(
            WorldClockWeatherLocation location,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Weather observations are not used by this test service.");
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }
}
