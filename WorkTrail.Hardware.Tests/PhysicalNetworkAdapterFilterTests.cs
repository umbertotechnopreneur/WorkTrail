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
using WorkTrail.Hardware;
using Xunit;

namespace WorkTrail.Hardware.Tests;

public sealed class PhysicalNetworkAdapterFilterTests
{
    private const string PhysicalAdapterId = "f5c75f1d-d3da-4eeb-9b25-b2a451a73fc7";

    [Fact]
    public void IsPhysicalNetworkAdapter_AcceptsKnownHardwareInterfaceGuid()
    {
        var physicalAdapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PhysicalAdapterId };

        Assert.True(LibreHardwareTelemetryCollector.IsPhysicalNetworkAdapter(
            "/nic/%7BF5C75F1D-D3DA-4EEB-9B25-B2A451A73FC7%7D", physicalAdapterIds));
    }

    [Theory]
    [InlineData("/nic/%7B3E9E694C-601D-4433-8304-C59F34EF5BC5%7D")]
    [InlineData("/nic/%7B21F44D61-A5E9-4953-8B95-112570733A6E%7D")]
    [InlineData("/nic/not-a-guid")]
    [InlineData("/gpu/0")]
    public void IsPhysicalNetworkAdapter_RejectsUnknownVirtualOrMalformedIdentifiers(string hardwareIdentifier)
    {
        var physicalAdapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PhysicalAdapterId };

        Assert.False(LibreHardwareTelemetryCollector.IsPhysicalNetworkAdapter(hardwareIdentifier, physicalAdapterIds));
    }

    [Fact]
    public void IsPhysicalNetworkAdapter_FailsClosedWhenWindowsInventoryIsUnavailable()
    {
        Assert.False(LibreHardwareTelemetryCollector.IsPhysicalNetworkAdapter(
            "/nic/%7BF5C75F1D-D3DA-4EEB-9B25-B2A451A73FC7%7D", null));
    }
}
