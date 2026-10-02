// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Product access tiers, independent of the purchase channel.</summary>
public enum ProductTier { Free, Premium }

/// <summary>Stable feature identities used by the catalog, presentation and application guards.</summary>
public enum ProductFeature { Tracking, Screenshots, Search, DataTransfer, WorldClocks, Astronomy, HardwareSensors, ActivityLabels, Cli, ReportExport, ScreenshotSchedule }

/// <summary>Declares one feature and the settings operations that require its entitlement.</summary>
public sealed record FeatureDefinition(ProductFeature Id, string TitleKey, ProductTier RequiredTier, IReadOnlyList<string> SettingKeys);

/// <summary>The runtime's current access state. This DTO is never persisted as a license.</summary>
public sealed record FeatureAccessSnapshot(ProductTier Tier, bool IsDebugSimulation, bool CanSimulate);

/// <summary>Supplies a verified license tier from a trusted runtime-owned licensing integration.</summary>
public interface IFeatureLicenseSource
{
    /// <summary>Gets the validated tier; implementations must report verification failures explicitly.</summary>
    ProductTier Tier { get; }
}

/// <summary>The explicit unlicensed state until a commercial license provider is configured.</summary>
internal sealed class UnlicensedFeatureSource : IFeatureLicenseSource
{
    /// <inheritdoc />
    public ProductTier Tier => ProductTier.Free;
}

/// <summary>Single source of feature classification; adding a premium setting requires no UI license conditionals.</summary>
public static class FeatureCatalog
{
    /// <summary>Gets all declared features and their access requirements.</summary>
    public static IReadOnlyList<FeatureDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new FeatureDefinition(ProductFeature.Tracking, "Main.TrackingStatus", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.Screenshots, "Screenshots.Title", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.Search, "Search.Title", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.DataTransfer, "Operations.InstallationTransfer.Title", ProductTier.Premium, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.WorldClocks, "WorldClock.OpenWindow", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.Astronomy, "Celestial.Agenda.Title", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.HardwareSensors, "Sensors.Open", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.Cli, "Cli.Title", ProductTier.Premium, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.ReportExport, "Export.Title", ProductTier.Premium, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.ScreenshotSchedule, "Schedule.WindowTitle", ProductTier.Premium,
            Array.AsReadOnly(new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" }
                .SelectMany(day => new[] { $"active_hours.{day}.active", $"active_hours.{day}.breaks" })
                .Append("screenshots.interval_minutes").ToArray())),
        new FeatureDefinition(ProductFeature.ActivityLabels, "Labels.Title", ProductTier.Free,
            Array.AsReadOnly(new[] { "activity.label.save", "activity.label.delete", "activity.label.select" }))
    });

    /// <summary>Resolves a feature and rejects undeclared identities.</summary>
    public static FeatureDefinition Get(ProductFeature feature) => Definitions.Single(item => item.Id == feature);

    /// <summary>Evaluates the same tier rule for application guards and passive presentation.</summary>
    public static bool IsAllowed(ProductFeature feature, FeatureAccessSnapshot access) => access.Tier >= Get(feature).RequiredTier;
}

/// <summary>Owns entitlement decisions. Debug overrides live only in memory in the shared runtime.</summary>
public sealed class FeatureAccessPolicy
{
    /// <summary>Maximum saved-label count when creating labels in the Free tier.</summary>
    public const int FreeLabelLimit = 3;

    /// <summary>Maximum saved world-clock count when adding clocks in the Free tier.</summary>
    public const int FreeClockLimit = 3;

    private readonly IFeatureLicenseSource _license;
#if DEBUG
    private int _debugTier = -1;
#endif

    /// <summary>Creates a policy using the runtime's trusted license source; no configured source means Free.</summary>
    public FeatureAccessPolicy(IFeatureLicenseSource? license = null) => _license = license ?? new UnlicensedFeatureSource();

    /// <summary>Gets an immutable snapshot without exposing a writable persisted entitlement.</summary>
    public FeatureAccessSnapshot Snapshot
    {
        get
        {
#if DEBUG
            var simulated = Volatile.Read(ref _debugTier);
            if (simulated >= 0) return new FeatureAccessSnapshot((ProductTier)simulated, true, true);
#endif
            var tier = _license.Tier;
            if (!Enum.IsDefined(tier)) throw new InvalidOperationException("The license source returned an unsupported tier.");
            return new FeatureAccessSnapshot(tier, false,
#if DEBUG
                true
#else
                false
#endif
            );
        }
    }

    /// <summary>Rejects protected settings before validation or persistence, regardless of frontend.</summary>
    public ValidationIssue? DeniedSetting(SettingsPatch patch, AppSettings settings)
    {
        var access = Snapshot;
        foreach (var rawKey in patch.Values.Keys)
        {
            var key = rawKey?.Trim().ToLowerInvariant();
            if (access.Tier == ProductTier.Free && (key is "retention.data_days" or "retention.screenshots_days")
                && int.TryParse(patch.Values[rawKey!], out var retentionDays) && retentionDays > 30)
                return new ValidationIssue(rawKey!, "premium_required", "Premium.Required");
            var feature = FeatureCatalog.Definitions.FirstOrDefault(item => item.SettingKeys.Contains(key));
            if (feature is not null && !FeatureCatalog.IsAllowed(feature.Id, access))
                return new ValidationIssue(rawKey!, "premium_required", "Premium.Required");
        }
        return null;
    }

    /// <summary>Checks the complete validated mutation; downgrade preserves existing labels and their selection.</summary>
    public ValidationIssue? DeniedSettingsChange(AppSettings previous, AppSettings updated)
    {
        var previousCount = previous.ActivityLabels?.Count ?? 0;
        var updatedCount = updated.ActivityLabels?.Count ?? 0;
        // Enforce the quota under the facade mutation lock, after every command in the patch has been applied.
        // Existing over-limit catalogs may still be edited or reduced without deleting user data on downgrade.
        if (Snapshot.Tier != ProductTier.Free) return null;
        if (updatedCount > FreeLabelLimit && updatedCount > previousCount)
            return new ValidationIssue("activity.label.save", "label_limit", "Labels.FreeLimit");
        var previousClocks = Services.WorldClockSelection.NormalizePersisted(previous.WorldClockCityIds).Count;
        var updatedClocks = Services.WorldClockSelection.NormalizePersisted(updated.WorldClockCityIds).Count;
        return updatedClocks > previousClocks ? DeniedWorldClockCreation(updatedClocks - 1) : null;
    }

    /// <summary>Checks a clock addition against the current runtime tier without mutating existing clocks.</summary>
    public ValidationIssue? DeniedWorldClockCreation(int currentCount) =>
        Snapshot.Tier == ProductTier.Free && currentCount >= FreeClockLimit
            ? new ValidationIssue("world_clock.cities", "clock_limit", "WorldClock.FreeLimit")
            : null;

#if DEBUG
    /// <summary>Sets a process-local simulation; this method does not exist in Release builds.</summary>
    public void Simulate(ProductTier tier)
    {
        if (!Enum.IsDefined(tier)) throw new ArgumentOutOfRangeException(nameof(tier));
        Volatile.Write(ref _debugTier, (int)tier);
    }
#endif
}
