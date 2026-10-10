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
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class HardwareTelemetryProtocolTests
{
    [Fact]
    public async Task Snapshot_RoundTripsNullableValuesAndUnits()
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new SystemSnapshot(now, "partial",
            [new("/battery", "Battery", "Battery", now, [new("/battery/power/0", "Discharge Rate", "Power", "W", 8.25),
                new("/battery/temp/0", "Battery Temperature", "Temperature", "°C", null)])]);
        using var stream = new MemoryStream();
        await HardwareTelemetryProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;

        var actual = await HardwareTelemetryProtocol.ReadAsync<SystemSnapshot>(stream, CancellationToken.None);

        SystemSnapshotValidator.Validate(actual);
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.Equal(expected.Devices[0].Sensors, actual.Devices[0].Sensors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(HardwareTelemetryProtocol.MaximumMessageBytes + 1)]
    public async Task InvalidLength_IsRejectedBeforePayloadAllocation(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedFrame_IsRejected()
    {
        using var stream = new MemoryStream(new byte[] { 8, 0, 0, 0, 123 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownCommandProperties_AreRejected()
    {
        var payload = Encoding.UTF8.GetBytes("{\"Version\":2,\"Command\":\"sample\",\"SamplingProfile\":\"normal\",\"path\":\"unexpected\"}");
        using var stream = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Position = 0;
        await Assert.ThrowsAsync<JsonException>(() => HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task VersionTwoRequest_RoundTripsProfile()
    {
        using var stream = new MemoryStream();
        var request = new HardwareCollectorRequest(HardwareTelemetryProtocol.Version, "sample", "fastest");
        await HardwareTelemetryProtocol.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(2, HardwareTelemetryProtocol.Version);
        Assert.Equal(request, await HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task MissingSamplingProfile_IsRejected()
    {
        using var stream = new MemoryStream();
        await HardwareTelemetryProtocol.WriteAsync(stream, new { Version = 2, Command = "sample" }, CancellationToken.None);
        stream.Position = 0;
        await Assert.ThrowsAsync<JsonException>(() => HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(stream, CancellationToken.None));
    }
}
