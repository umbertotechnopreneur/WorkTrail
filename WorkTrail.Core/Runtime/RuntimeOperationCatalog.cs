// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.Reflection;

namespace WorkTrail.Runtime;

/// <summary>Identifies every operation supported by the versioned local runtime protocol.</summary>
internal enum RuntimeOperation
{
    [RuntimeOperationWireName("runtime.health")]
    RuntimeHealth,
    [RuntimeOperationWireName("tracking.start")]
    TrackingStart,
    [RuntimeOperationWireName("tracking.pause")]
    TrackingPause,
    [RuntimeOperationWireName("tracking.toggle")]
    TrackingToggle,
    [RuntimeOperationWireName("dashboard.get")]
    DashboardGet,
    [RuntimeOperationWireName("world_clocks.get.v3")]
    WorldClocksGetV3,
    [RuntimeOperationWireName("celestial.reference.v1")]
    CelestialReferenceV1,
    [RuntimeOperationWireName("celestial.get.v1")]
    CelestialGetV1,
    [RuntimeOperationWireName("celestial.map.v1")]
    CelestialMapV1,
    [RuntimeOperationWireName("world_clocks.convert.v2")]
    WorldClocksConvertV2,
    [RuntimeOperationWireName("world_clocks.catalog.v1")]
    WorldClocksCatalogV1,
    [RuntimeOperationWireName("world_clocks.add.v3")]
    WorldClocksAddV3,
    [RuntimeOperationWireName("world_clocks.remove.v3")]
    WorldClocksRemoveV3,
    [RuntimeOperationWireName("world_clocks.move.v1")]
    WorldClocksMoveV1,
    [RuntimeOperationWireName("world_clocks.weather.key.set.v2")]
    WorldClocksWeatherKeySetV2,
    [RuntimeOperationWireName("session.last")]
    SessionLast,
    [RuntimeOperationWireName("session.today")]
    SessionToday,
    [RuntimeOperationWireName("search.query.v1")]
    SearchQueryV1,
    [RuntimeOperationWireName("search.availability.v1")]
    SearchAvailabilityV1,
    [RuntimeOperationWireName("search.rebuild.v1")]
    SearchRebuildV1,
    [RuntimeOperationWireName("system.snapshot")]
    SystemSnapshot,
    [RuntimeOperationWireName("hardware.snapshot.v1")]
    HardwareSnapshotV1,
    [RuntimeOperationWireName("hardware.advanced.enable.v1")]
    HardwareAdvancedEnableV1,
    [RuntimeOperationWireName("screenshot.capture")]
    ScreenshotCapture,
    [RuntimeOperationWireName("screenshot.manual.capture")]
    ScreenshotManualCapture,
    [RuntimeOperationWireName("screenshot.manual.delete")]
    ScreenshotManualDelete,
    [RuntimeOperationWireName("screenshot.analyze")]
    ScreenshotAnalyze,
    [RuntimeOperationWireName("screenshot.latest")]
    ScreenshotLatest,
    [RuntimeOperationWireName("screenshot.gallery")]
    ScreenshotGallery,
    [RuntimeOperationWireName("screenshot.gallery.latest")]
    ScreenshotGalleryLatest,
    [RuntimeOperationWireName("screenshot.image.get.v1")]
    ScreenshotImageGetV1,
    [RuntimeOperationWireName("installations.list.v1")]
    InstallationsListV1,
    [RuntimeOperationWireName("installations.update.v1")]
    InstallationsUpdateV1,
    [RuntimeOperationWireName("archive.export.v1")]
    ArchiveExportV1,
    [RuntimeOperationWireName("archive.import.preview.v1")]
    ArchiveImportPreviewV1,
    [RuntimeOperationWireName("archive.import.merge.v1")]
    ArchiveImportMergeV1,
    [RuntimeOperationWireName("archive.progress.v1")]
    ArchiveProgressV1,
    [RuntimeOperationWireName("screenshot.delete")]
    ScreenshotDelete,
    [RuntimeOperationWireName("screenshot.analysis.delete.v1")]
    ScreenshotAnalysisDeleteV1,
    [RuntimeOperationWireName("screenshot.save")]
    ScreenshotSave,
    [RuntimeOperationWireName("screenshot.share")]
    ScreenshotShare,
    [RuntimeOperationWireName("diagnostics.log.open")]
    DiagnosticsLogOpen,
    [RuntimeOperationWireName("diagnostics.log.open_folder")]
    DiagnosticsLogOpenFolder,
    [RuntimeOperationWireName("diagnostics.log.share")]
    DiagnosticsLogShare,
    [RuntimeOperationWireName("screenshot.open_folder")]
    ScreenshotOpenFolder,
    [RuntimeOperationWireName("notifications.drain")]
    NotificationsDrain,
    [RuntimeOperationWireName("ai.status")]
    AiStatus,
    [RuntimeOperationWireName("ai.key.get")]
    AiKeyGet,
    [RuntimeOperationWireName("ai.pricing.overview")]
    AiPricingOverview,
    [RuntimeOperationWireName("ai.connection.test")]
    AiConnectionTest,
    [RuntimeOperationWireName("ai.screenshot_reprocess.preview.v1")]
    AiScreenshotReprocessPreviewV1,
    [RuntimeOperationWireName("ai.screenshot_reprocess.start.v1")]
    AiScreenshotReprocessStartV1,
    [RuntimeOperationWireName("ai.screenshot_reprocess.status.v1")]
    AiScreenshotReprocessStatusV1,
    [RuntimeOperationWireName("ai.screenshot_reprocess.pause.v1")]
    AiScreenshotReprocessPauseV1,
    [RuntimeOperationWireName("ai.screenshot_reprocess.resume.v1")]
    AiScreenshotReprocessResumeV1,
    [RuntimeOperationWireName("ai.models")]
    AiModels,
    [RuntimeOperationWireName("ai.enable")]
    AiEnable,
    [RuntimeOperationWireName("ai.disable")]
    AiDisable,
    [RuntimeOperationWireName("ai.configure")]
    AiConfigure,
    [RuntimeOperationWireName("ai.key.set")]
    AiKeySet,
    [RuntimeOperationWireName("ai.analyze")]
    AiAnalyze,
    [RuntimeOperationWireName("report.query.v1")]
    ReportQueryV1,
    [RuntimeOperationWireName("report.export.setup.v1")]
    ReportExportSetupV1,
    [RuntimeOperationWireName("report.export.preview.v1")]
    ReportExportPreviewV1,
    [RuntimeOperationWireName("report.export.preferences.v1")]
    ReportExportPreferencesV1,
    [RuntimeOperationWireName("report.export.write.v1")]
    ReportExportWriteV1,
    [RuntimeOperationWireName("report.export.summary.v1")]
    ReportExportSummaryV1,
    [RuntimeOperationWireName("ui.open")]
    UiOpen,
    [RuntimeOperationWireName("privacy.list")]
    PrivacyList,
    [RuntimeOperationWireName("privacy.add")]
    PrivacyAdd,
    [RuntimeOperationWireName("privacy.remove")]
    PrivacyRemove,
    [RuntimeOperationWireName("privacy.test_current")]
    PrivacyTestCurrent,
    [RuntimeOperationWireName("retention.status")]
    RetentionStatus,
    [RuntimeOperationWireName("retention.preview")]
    RetentionPreview,
    [RuntimeOperationWireName("retention.run")]
    RetentionRun,
    [RuntimeOperationWireName("app.atomic_reset.v1")]
    AppAtomicResetV1,
    [RuntimeOperationWireName("plugins.list")]
    PluginsList,
    [RuntimeOperationWireName("plugins.show")]
    PluginsShow,
    [RuntimeOperationWireName("plugins.enable")]
    PluginsEnable,
    [RuntimeOperationWireName("plugins.disable")]
    PluginsDisable,
    [RuntimeOperationWireName("settings.get")]
    SettingsGet,
    [RuntimeOperationWireName("features.access.get.v1")]
    FeatureAccessGetV1,
#if DEBUG
    [RuntimeOperationWireName("debug.features.simulate.v1")]
    DebugFeatureSimulateV1,
#endif
    [RuntimeOperationWireName("quick_setup.apply.v1")]
    QuickSetupApplyV1,
    [RuntimeOperationWireName("settings.patch")]
    SettingsPatch,
    [RuntimeOperationWireName("window.state.restore")]
    WindowStateRestore,
    [RuntimeOperationWireName("window.reveal_open.v1")]
    WindowRevealOpenV1,
    [RuntimeOperationWireName("window.state.save")]
    WindowStateSave,
    [RuntimeOperationWireName("window.open_state.set")]
    WindowOpenStateSet,
    [RuntimeOperationWireName("window.ocr_source.set")]
    WindowOcrSourceSet,
    [RuntimeOperationWireName("startup.status")]
    StartupStatus,
    [RuntimeOperationWireName("startup.enable")]
    StartupEnable,
    [RuntimeOperationWireName("startup.disable")]
    StartupDisable,
    [RuntimeOperationWireName("product.get")]
    ProductGet,
    [RuntimeOperationWireName("product.link.open")]
    ProductLinkOpen
}

/// <summary>Pairs a typed runtime operation with its stable wire name.</summary>
internal sealed record RuntimeOperationDefinition(RuntimeOperation Operation, string WireName);

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class RuntimeOperationWireNameAttribute : Attribute
{
    /// <summary>Creates a stable wire-name annotation.</summary>
    internal RuntimeOperationWireNameAttribute(string wireName)
    {
        if (string.IsNullOrWhiteSpace(wireName)
            || wireName.Length > 128
            || wireName.Any(static character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not ('.' or '_')))
        {
            throw new ArgumentException("Runtime operation wire names must use lowercase ASCII protocol characters.", nameof(wireName));
        }

        WireName = wireName;
    }

    /// <summary>Gets the exact stable value serialized on the wire.</summary>
    internal string WireName { get; }
}

/// <summary>Owns the one-to-one mapping shared by the runtime host and client.</summary>
internal static class RuntimeOperationCatalog
{
    private static readonly IReadOnlyList<RuntimeOperationDefinition> Definitions = CreateDefinitions();
    private static readonly IReadOnlyDictionary<string, RuntimeOperation> OperationsByWireName = BuildWireLookup(Definitions);
    private static readonly IReadOnlyDictionary<RuntimeOperation, string> WireNamesByOperation =
        new ReadOnlyDictionary<RuntimeOperation, string>(
            Definitions.ToDictionary(static definition => definition.Operation, static definition => definition.WireName));

    /// <summary>Gets every protocol operation exactly once.</summary>
    internal static IReadOnlyList<RuntimeOperationDefinition> All => Definitions;

    /// <summary>Resolves a typed operation from an untrusted request wire name.</summary>
    internal static bool TryResolve(string? wireName, out RuntimeOperation operation)
    {
        if (wireName is not null && OperationsByWireName.TryGetValue(wireName, out operation))
        {
            return true;
        }

        operation = default;
        return false;
    }

    /// <summary>Returns the exact wire name for a typed client operation.</summary>
    internal static string GetWireName(RuntimeOperation operation) =>
        WireNamesByOperation.TryGetValue(operation, out var wireName)
            ? wireName
            : throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown runtime operation.");

    /// <summary>Builds a lookup and rejects duplicate wire names immediately.</summary>
    internal static IReadOnlyDictionary<string, RuntimeOperation> BuildWireLookup(
        IEnumerable<RuntimeOperationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var lookup = new Dictionary<string, RuntimeOperation>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!lookup.TryAdd(definition.WireName, definition.Operation))
            {
                throw new InvalidOperationException($"Duplicate runtime operation wire name '{definition.WireName}'.");
            }
        }

        return new ReadOnlyDictionary<string, RuntimeOperation>(lookup);
    }

    private static IReadOnlyList<RuntimeOperationDefinition> CreateDefinitions()
    {
        var operations = Enum.GetValues<RuntimeOperation>();
        var definitions = operations.Select(operation =>
        {
            var member = typeof(RuntimeOperation).GetField(operation.ToString(), BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Runtime operation '{operation}' has no enum field.");
            var wireName = member.GetCustomAttribute<RuntimeOperationWireNameAttribute>()?.WireName
                ?? throw new InvalidOperationException($"Runtime operation '{operation}' has no wire name.");
            return new RuntimeOperationDefinition(operation, wireName);
        }).ToArray();
        if (definitions.Select(static definition => definition.Operation).Distinct().Count() != operations.Length)
        {
            throw new InvalidOperationException("The runtime operation catalog contains duplicate typed operations.");
        }

        _ = BuildWireLookup(definitions);
        return Array.AsReadOnly(definitions);
    }
}
