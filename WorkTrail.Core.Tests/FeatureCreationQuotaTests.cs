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
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks Free creation quotas and protected saves at the facade boundary using isolated synthetic settings.</summary>
public sealed class FeatureCreationQuotaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WorkTrail-label-access-" + Guid.NewGuid().ToString("N"));

    private LocalStore Store()
    {
        Directory.CreateDirectory(_root);
        var store = new LocalStore(_root);
        store.SaveSettings(store.LoadSettings() with
        {
            ScreenshotDirectory = Path.Combine(_root, "screenshots"),
            StartWithWindows = false,
            ActivityLabels = []
        });
        return store;
    }

    private static SettingsPatch Save(string name, string id = "") => Patch("activity.label.save",
        JsonSerializer.Serialize(new ActivityLabelDefinition(id, name, "work", "#FF6268")));

    private static SettingsPatch Patch(string key, string value) => new(new Dictionary<string, string?> { [key] = value });

    /// <summary>The fourth creation fails through both local and IPC calls, while rename/delete/select still work.</summary>
    [Fact]
    public async Task Free_FourthLabelDeniedByFacadeAndIpcWithoutChangingSavedSettings()
    {
        var store = Store();
        await using var app = Application(store, ProductTier.Free);
        for (var i = 1; i <= 3; i++) Assert.True((await app.PatchSettingsAsync(Save("Label " + i), CancellationToken.None)).Succeeded);
        var before = store.LoadSettings().ActivityLabels!.ToArray();
        var denied = await app.PatchSettingsAsync(Save("Fourth"), CancellationToken.None);
        Assert.Equal("feature.label_limit", denied.Code);
        Assert.Equal("Labels.FreeLimit", denied.MessageKey);
        var dispatcher = new RuntimeRequestDispatcher(app, NullLogger.Instance);
        var response = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), "settings.patch",
            JsonSerializer.SerializeToElement(Save("Via IPC"), RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
        Assert.Equal("feature.label_limit", response.Code);
        Assert.Equal(before, store.LoadSettings().ActivityLabels);
        Assert.True((await app.PatchSettingsAsync(Save("Renamed", before[0].Id), CancellationToken.None)).Succeeded);
        Assert.True((await app.PatchSettingsAsync(Patch("activity.label.select", before[0].Id), CancellationToken.None)).Succeeded);
        Assert.Equal("Renamed", store.LoadSettings().SpanLabel);
        Assert.True((await app.PatchSettingsAsync(Patch("activity.label.delete", before[1].Id), CancellationToken.None)).Succeeded);
        Assert.True((await app.PatchSettingsAsync(Save("Replacement"), CancellationToken.None)).Succeeded);
        Assert.Equal(3, store.LoadSettings().ActivityLabels!.Count);
    }

    /// <summary>Concurrent saves share one quota check and cannot both claim the third slot.</summary>
    [Fact]
    public async Task Free_ConcurrentCreationCannotExceedThree()
    {
        var store = Store();
        await using var app = Application(store, ProductTier.Free);
        Assert.True((await app.PatchSettingsAsync(Save("First"), CancellationToken.None)).Succeeded);
        Assert.True((await app.PatchSettingsAsync(Save("Second"), CancellationToken.None)).Succeeded);
        var results = await Task.WhenAll(app.PatchSettingsAsync(Save("Third A"), CancellationToken.None), app.PatchSettingsAsync(Save("Third B"), CancellationToken.None));
        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => result.Code == "feature.label_limit");
        Assert.Equal(3, store.LoadSettings().ActivityLabels!.Count);
    }

    /// <summary>Duplicate normalized commands are rejected atomically before they can bypass the quota.</summary>
    [Fact]
    public async Task Free_BatchedCreationCannotBypassQuotaWithNormalizedKeys()
    {
        var store = Store();
        await using var app = Application(store, ProductTier.Free);
        var keys = new[] { "activity.label.save", "ACTIVITY.LABEL.SAVE", " activity.label.save", "activity.label.save " };
        var patch = new SettingsPatch(keys.Select((key, i) => new KeyValuePair<string, string?>(key, Save("Label " + i).Values.Single().Value)).ToDictionary());
        var result = await app.PatchSettingsAsync(patch, CancellationToken.None);
        Assert.Equal("settings.validation.failed", result.Code);
        Assert.Empty(store.LoadSettings().ActivityLabels ?? []);
    }

    /// <summary>Premium can exceed three; reopening as Free preserves existing definitions and blocks new ones.</summary>
    [Fact]
    public async Task DowngradeAndRestart_PreserveExistingLabelsAndSelectionButBlockGrowth()
    {
        var store = Store();
        await using (var premium = Application(store, ProductTier.Premium))
        {
            for (var i = 1; i <= 4; i++) Assert.True((await premium.PatchSettingsAsync(Save("Label " + i), CancellationToken.None)).Succeeded);
            var fourth = store.LoadSettings().ActivityLabels![3];
            Assert.True((await premium.PatchSettingsAsync(Patch("activity.label.select", fourth.Id), CancellationToken.None)).Succeeded);
#if DEBUG
            Assert.True((await premium.SimulateFeatureAccessAsync(ProductTier.Free, CancellationToken.None)).Succeeded);
            Assert.Equal("Label 4", store.LoadSettings().SpanLabel);
            Assert.Equal("feature.label_limit", (await premium.PatchSettingsAsync(Save("Fifth"), CancellationToken.None)).Code);
#endif
        }
        await using var free = Application(store, ProductTier.Free);
        Assert.Equal("Label 4", store.LoadSettings().SpanLabel);
        Assert.Equal(4, store.LoadSettings().ActivityLabels!.Count);
        Assert.Equal("feature.label_limit", (await free.PatchSettingsAsync(Save("Fifth"), CancellationToken.None)).Code);
        Assert.True((await free.PatchSettingsAsync(Save("Kept", store.LoadSettings().ActivityLabels![3].Id), CancellationToken.None)).Succeeded);
    }

    /// <summary>Free clock quotas apply to catalogs, direct writes and IPC, while existing selections remain unchanged.</summary>
    [Fact]
    public async Task Free_FourthClockDeniedByFacadeAndIpc()
    {
        var store = Store();
        await using var app = Application(store, ProductTier.Free);
        var selected = WorldClockSelection.NormalizePersisted(store.LoadSettings().WorldClockCityIds).ToArray();
        Assert.Equal(3, selected.Length);
        var catalog = await app.GetWorldClockCityCatalogAsync(CancellationToken.None);
        Assert.True(catalog.Succeeded);
        Assert.Equal("WorldClock.FreeLimit", catalog.Value!.AddDeniedMessageKey);
        var next = catalog.Value.Cities.First(city => !selected.Contains(city.Id)).Id;
        Assert.Equal("feature.clock_limit", (await app.AddWorldClockAsync(next, CancellationToken.None)).Code);
        var dispatcher = new RuntimeRequestDispatcher(app, NullLogger.Instance);
        var result = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), "world_clocks.add.v3",
            JsonSerializer.SerializeToElement(new { CityId = next }, RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
        Assert.Equal("feature.clock_limit", result.Code);
        Assert.Equal(selected, store.LoadSettings().WorldClockCityIds);
        Assert.True((await app.RemoveWorldClockAsync(selected[2], CancellationToken.None)).Succeeded);
        Assert.Null((await app.GetWorldClockCityCatalogAsync(CancellationToken.None)).Value!.AddDeniedMessageKey);
        Assert.True((await app.AddWorldClockAsync(next, CancellationToken.None)).Succeeded);
        Assert.Equal(3, store.LoadSettings().WorldClockCityIds!.Count);
    }

    /// <summary>A picker opened in Premium cannot add a fourth clock after the runtime switches to Free.</summary>
    [Fact]
    public async Task Premium_CanAddFourthClockAndFreeRestartPreservesItWithoutAllowingAnother()
    {
        var store = Store();
        string next;
        await using (var premium = Application(store, ProductTier.Premium))
        {
            var catalog = (await premium.GetWorldClockCityCatalogAsync(CancellationToken.None)).Value!;
            Assert.Null(catalog.AddDeniedMessageKey);
            var available = catalog.Cities.Where(city => !WorldClockSelection.NormalizePersisted(store.LoadSettings().WorldClockCityIds).Contains(city.Id)).Take(2).ToArray();
            Assert.True((await premium.AddWorldClockAsync(available[0].Id, CancellationToken.None)).Succeeded);
            next = available[1].Id;
#if DEBUG
            Assert.True((await premium.SimulateFeatureAccessAsync(ProductTier.Free, CancellationToken.None)).Succeeded);
            Assert.Equal("feature.clock_limit", (await premium.AddWorldClockAsync(next, CancellationToken.None)).Code);
#endif
        }
        await using var free = Application(store, ProductTier.Free);
        Assert.Equal(4, store.LoadSettings().WorldClockCityIds!.Count);
        Assert.Equal("feature.clock_limit", (await free.AddWorldClockAsync(next, CancellationToken.None)).Code);
    }

    /// <summary>The same schedule save is denied in Free and accepted in Premium, including direct IPC calls.</summary>
    [Theory]
    [InlineData(ProductTier.Free, false)]
    [InlineData(ProductTier.Premium, true)]
    public async Task ScreenshotSchedule_SaveRequiresPremium(ProductTier tier, bool allowed)
    {
        var store = Store();
        var interval = store.LoadSettings().ScreenshotIntervalMinutes;
        var patch = Patch("screenshots.interval_minutes", interval == 17 ? "18" : "17");
        await using var app = Application(store, tier);
        var direct = await app.PatchSettingsAsync(patch, CancellationToken.None);
        Assert.Equal(allowed, direct.Succeeded);
        var dispatcher = new RuntimeRequestDispatcher(app, NullLogger.Instance);
        var ipc = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), "settings.patch",
            JsonSerializer.SerializeToElement(patch, RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
        Assert.Equal(allowed, ipc.Succeeded);
        if (!allowed)
        {
            Assert.Equal("feature.premium_required", direct.Code);
            Assert.Equal("feature.premium_required", ipc.Code);
            Assert.Equal(interval, store.LoadSettings().ScreenshotIntervalMinutes);
        }
    }

    /// <summary>Free can inspect a synthetic archive, but only Premium can write or commit its import.</summary>
    [Theory]
    [InlineData(ProductTier.Free, false)]
    [InlineData(ProductTier.Premium, true)]
    public async Task Archives_PreviewRemainsFreeButExecutionRequiresPremium(ProductTier tier, bool allowed)
    {
        var store = Store();
        var source = new LocalStore(Path.Combine(_root, "source"));
        var archivePath = Path.Combine(_root, "source.tmuarchive");
        new DataArchiveService(source).Export(new(archivePath, IncludeScreenshots: false), CancellationToken.None);
        await using var app = Application(store, tier);
        var preview = await app.PreviewDataArchiveImportAsync(new(archivePath), CancellationToken.None);
        Assert.True(preview.Succeeded);
        Assert.NotNull(preview.Value);
        var destination = Path.Combine(_root, "output.tmuarchive");
        var exported = await app.ExportDataArchiveAsync(new(destination, IncludeScreenshots: false), CancellationToken.None);
        Assert.Equal(allowed, exported.Succeeded);
        Assert.Equal(allowed, File.Exists(destination));
        var profilesBefore = store.GetInstallationProfiles().Count;
        var imported = await app.ImportDataArchiveAsync(new(preview.Value.PlanId), CancellationToken.None);
        Assert.Equal(allowed, imported.Succeeded);
        if (!allowed)
        {
            Assert.Equal("feature.premium_required", exported.Code);
            Assert.Equal("feature.premium_required", imported.Code);
            Assert.Equal(profilesBefore, store.GetInstallationProfiles().Count);
            var dispatcher = new RuntimeRequestDispatcher(app, NullLogger.Instance);
            var exportIpc = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(),
                RuntimeOperationCatalog.GetWireName(RuntimeOperation.ArchiveExportV1),
                JsonSerializer.SerializeToElement(new DataArchiveExportRequest(destination, IncludeScreenshots: false), RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
            Assert.Equal("feature.premium_required", exportIpc.Code);
            var importIpc = await dispatcher.DispatchAsync(new(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(),
                RuntimeOperationCatalog.GetWireName(RuntimeOperation.ArchiveImportMergeV1),
                JsonSerializer.SerializeToElement(new DataArchiveImportRequest(preview.Value.PlanId), RuntimeProtocol.SerializerOptions), "en-US", null), CancellationToken.None);
            Assert.Equal("feature.premium_required", importIpc.Code);
            Assert.False(File.Exists(destination));
        }
#if DEBUG
        else
        {
            var nextPlan = await app.PreviewDataArchiveImportAsync(new(archivePath), CancellationToken.None);
            Assert.True((await app.SimulateFeatureAccessAsync(ProductTier.Free, CancellationToken.None)).Succeeded);
            Assert.Equal("feature.premium_required", (await app.ImportDataArchiveAsync(new(nextPlan.Value!.PlanId), CancellationToken.None)).Code);
        }
#endif
    }

    private static WorkTrailApplication Application(LocalStore store, ProductTier tier) =>
        new(store, new UtilityService(), new TrackingDomainService(store), DispatchProxy.Create<IScreenCaptureService, NoExternalCalls>(),
            new FakeHardwareTelemetryService(), DispatchProxy.Create<IAiAnalysisService, NoExternalCalls>(), new StartupService(), new BuildInformationService(),
            worldClockService: new WorldClockService(CityCatalogPath()), startScheduledSnapshotTimer: false, featureLicenseSource: new License(tier));

    private static string CityCatalogPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
                return Path.Combine(directory.FullName, "WorkTrail", "Assets", "WorldClocks", "world-clocks.sqlite3");
        throw new DirectoryNotFoundException("Could not locate the distributed world-clock catalog.");
    }

    /// <summary>Rejects any accidental capture or provider work during settings checks.</summary>
    public class NoExternalCalls : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Unexpected external work in label test.");
    }

    private sealed class License(ProductTier tier) : IFeatureLicenseSource
    {
        /// <inheritdoc />
        public ProductTier Tier => tier;
    }

    /// <summary>Removes only this fixture's synthetic settings and database.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
