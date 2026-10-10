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


using System.Security.Cryptography;
using System.Text;

namespace WorkTrail.Application;

/// <summary>Defines the supported appearance and validation contract for installation provenance.</summary>
public static class InstallationProfileCatalog
{
    /// <summary>Gets the stable color palette accepted by persistence, IPC, and presentation clients.</summary>
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#5B8DEF",
        "#6BBF8A",
        "#E88F6B",
        "#A97BEA",
        "#E0B84D",
        "#5CC2C7",
        "#E36D8D",
        "#8A9AAE",
        "#B23A48",
        "#3157C8",
        "#2D7D46",
        "#5B4DB7",
        "#B85C24",
        "#167C80",
        "#A23B72",
        "#7A553B"
    ];

    /// <summary>Gets the stable icon identifiers accepted by persistence, IPC, and presentation clients.</summary>
    public static IReadOnlyList<string> Icons { get; } =
    [
        "desktop",
        "laptop",
        "workstation",
        "home",
        "tablet",
        "phone",
        "server",
        "cloud",
        "office",
        "briefcase",
        "terminal",
        "gaming",
        "travel",
        "school",
        "studio",
        "camera"
    ];

    /// <summary>Creates the deterministic first profile for a newly discovered installation.</summary>
    public static InstallationProfile CreateDefault(
        string installationId,
        string machineName,
        DateTimeOffset observedAt)
    {
        var normalizedId = NormalizeInstallationId(installationId);
        var normalizedMachineName = NormalizeMachineName(machineName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedId));
        return new InstallationProfile(
            normalizedId,
            normalizedMachineName,
            normalizedMachineName,
            Colors[hash[0] % Colors.Count],
            Icons[hash[1] % Icons.Count],
            observedAt.ToUniversalTime(),
            observedAt.ToUniversalTime(),
            Revision: 1);
    }

    /// <summary>Validates and applies editable appearance without changing immutable machine provenance.</summary>
    public static OperationResult<InstallationProfile> Apply(
        InstallationProfile existing,
        UpdateInstallationProfileRequest request,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(existing.InstallationId, request.InstallationId, StringComparison.Ordinal))
        {
            return OperationResult<InstallationProfile>.Failure(
                "installation.profile.identity_mismatch",
                "InstallationProfileIdentityMismatch",
                new ValidationIssue("installationId", "mismatch", "InstallationProfileIdentityMismatch"));
        }

        var friendlyName = request.FriendlyName?.Trim() ?? string.Empty;
        if (friendlyName.Length is < 1 or > 64)
        {
            return OperationResult<InstallationProfile>.Failure(
                "installation.profile.invalid",
                "InstallationProfileInvalid",
                new ValidationIssue("friendlyName", "length", "InstallationProfileFriendlyNameInvalid"));
        }

        var color = request.Color?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Colors.Contains(color, StringComparer.Ordinal))
        {
            return OperationResult<InstallationProfile>.Failure(
                "installation.profile.invalid",
                "InstallationProfileInvalid",
                new ValidationIssue("color", "unsupported", "InstallationProfileColorInvalid"));
        }

        var icon = request.Icon?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Icons.Contains(icon, StringComparer.Ordinal))
        {
            return OperationResult<InstallationProfile>.Failure(
                "installation.profile.invalid",
                "InstallationProfileInvalid",
                new ValidationIssue("icon", "unsupported", "InstallationProfileIconInvalid"));
        }

        var value = existing with
        {
            FriendlyName = friendlyName,
            Color = color,
            Icon = icon,
            UpdatedAt = updatedAt.ToUniversalTime(),
            Revision = checked(existing.Revision + 1)
        };
        return OperationResult<InstallationProfile>.Success(
            "installation.profile.valid",
            "InstallationProfileValid",
            value);
    }

    /// <summary>Rejects malformed persisted or imported profile data without silently normalizing it.</summary>
    public static InstallationProfile ValidatePersisted(InstallationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var installationId = NormalizeInstallationId(profile.InstallationId);
        var machineName = NormalizeMachineName(profile.MachineName);
        if (profile.FriendlyName != profile.FriendlyName.Trim()
            || profile.FriendlyName.Length is < 1 or > 64
            || !Colors.Contains(profile.Color, StringComparer.Ordinal)
            || !Icons.Contains(profile.Icon, StringComparer.Ordinal)
            || profile.FirstSeenAt.Offset != TimeSpan.Zero
            || profile.UpdatedAt.Offset != TimeSpan.Zero
            || profile.UpdatedAt < profile.FirstSeenAt
            || profile.Revision < 1)
        {
            throw new InvalidDataException("The installation profile is invalid.");
        }

        return profile with
        {
            InstallationId = installationId,
            MachineName = machineName,
            IsCurrent = false
        };
    }

    private static string NormalizeInstallationId(string installationId)
    {
        if (!Guid.TryParseExact(installationId, "N", out var parsed))
        {
            throw new InvalidDataException("The installation identifier is invalid.");
        }

        return parsed.ToString("N");
    }

    private static string NormalizeMachineName(string machineName)
    {
        var normalized = machineName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128)
        {
            throw new InvalidDataException("The installation machine name is invalid.");
        }

        return normalized;
    }
}
