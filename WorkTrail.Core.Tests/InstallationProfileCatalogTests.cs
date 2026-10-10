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
using System.Linq;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class InstallationProfileCatalogTests
{
    [Fact]
    public void AppearanceCatalog_HasSixteenStableUniqueChoices()
    {
        Assert.Equal(16, InstallationProfileCatalog.Colors.Count);
        Assert.Equal(16, InstallationProfileCatalog.Icons.Count);
        Assert.Equal(16, InstallationProfileCatalog.Colors.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16, InstallationProfileCatalog.Icons.Distinct(StringComparer.Ordinal).Count());
        Assert.All(InstallationProfileCatalog.Colors, color =>
            Assert.Matches("^#[0-9A-F]{6}$", color));
        Assert.All(InstallationProfileCatalog.Icons, icon =>
            Assert.Matches("^[a-z]+$", icon));
    }

    [Fact]
    public void CreateDefault_SelectsColorAndIconDeterministically()
    {
        const string installationId = "00112233445566778899aabbccddeeff";
        var observedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        var first = InstallationProfileCatalog.CreateDefault(installationId, "WORKSTATION", observedAt);
        var second = InstallationProfileCatalog.CreateDefault(installationId, "WORKSTATION", observedAt);

        Assert.Equal(first.Color, second.Color);
        Assert.Equal(first.Icon, second.Icon);
        Assert.Equal("#3157C8", first.Color);
        Assert.Equal("cloud", first.Icon);
        Assert.Contains(first.Color, InstallationProfileCatalog.Colors);
        Assert.Contains(first.Icon, InstallationProfileCatalog.Icons);
    }
}
