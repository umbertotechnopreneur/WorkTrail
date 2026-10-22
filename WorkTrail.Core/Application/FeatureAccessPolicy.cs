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

/// <summary>Product access tiers, independent of the purchase channel.</summary>
public enum ProductTier
{
    Free,
    Premium
}

/// <summary>Stable feature identities used by the catalog, presentation and application guards.</summary>
public enum ProductFeature
{
    WorldClocks,
    Astronomy
}

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

    #region Methods and properties
    /// <summary>Gets all declared features and their access requirements.</summary>
    public static IReadOnlyList<FeatureDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new FeatureDefinition(ProductFeature.WorldClocks, "WorldClock.OpenWindow", ProductTier.Free, Array.Empty<string>()),
        new FeatureDefinition(ProductFeature.Astronomy, "Celestial.Agenda.Title", ProductTier.Free, Array.Empty<string>()),
    });

    /// <summary>Resolves a feature and rejects undeclared identities.</summary>
    public static FeatureDefinition Get(ProductFeature feature) => Definitions.Single(item => item.Id == feature);

    /// <summary>Evaluates the same tier rule for application guards and passive presentation.</summary>
    public static bool IsAllowed(ProductFeature feature, FeatureAccessSnapshot access) => access.Tier >= Get(feature).RequiredTier;

    #endregion

}

/// <summary>Owns entitlement decisions. Debug overrides live only in memory in the shared runtime.</summary>
public sealed class FeatureAccessPolicy
{
    #region Fields

    /// <summary>Maximum saved world-clock count when adding clocks in the Free tier.</summary>
    public const int FreeClockLimit = 3;

    private readonly IFeatureLicenseSource _license;
#if DEBUG
    private int _debugTier = -1;
#endif

    #endregion

    #region Initialization

    /// <summary>Creates a policy using the runtime's trusted license source; no configured source means Free.</summary>
    public FeatureAccessPolicy(IFeatureLicenseSource? license = null) => _license = license ?? new UnlicensedFeatureSource();

    #endregion

    #region Methods and properties

    /// <summary>Gets an immutable snapshot without exposing a writable persisted entitlement.</summary>
    public FeatureAccessSnapshot Snapshot
    {
        get
        {
#if DEBUG
            var simulated = Volatile.Read(ref _debugTier);
            if (simulated >= 0)
            {
                return new FeatureAccessSnapshot((ProductTier)simulated, true, true);
            }
#endif
            var tier = _license.Tier;
            if (!Enum.IsDefined(tier))
            {
                throw new InvalidOperationException("The license source returned an unsupported tier.");
            }

            return new FeatureAccessSnapshot(tier, false,
#if DEBUG
                true
#else
                false
#endif
            );
        }
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
        if (!Enum.IsDefined(tier))
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }
        Volatile.Write(ref _debugTier, (int)tier);
    }
#endif
    #endregion
}
