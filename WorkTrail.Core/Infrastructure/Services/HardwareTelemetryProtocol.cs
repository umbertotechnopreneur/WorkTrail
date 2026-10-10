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


using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace WorkTrail.Services;

/// <summary>Defines the deliberately small, read-only hardware-helper protocol.</summary>
public static class HardwareTelemetryProtocol
{
    /// <summary>The only supported protocol version; incompatible helpers fail before polling.</summary>
    public const int Version = 2;
    /// <summary>Bounds memory allocated for an incoming telemetry frame.</summary>
    public const int MaximumMessageBytes = 524_288;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    /// <summary>Writes a bounded length-prefixed JSON frame.</summary>
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length is 0 or > MaximumMessageBytes) throw new InvalidDataException("Hardware frame exceeds its size limit.");
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a bounded frame and rejects truncated, oversized and malformed input.</summary>
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Hardware frame length is invalid.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new InvalidDataException("Hardware frame is null.");
    }

    /// <summary>Checks the actual client process before a parent sends any commands or accepts sensor data.</summary>
    public static void VerifyClientProcess(NamedPipeServerStream pipe, int expectedProcessId)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId) || processId != expectedProcessId)
            throw new UnauthorizedAccessException("Unexpected hardware-helper client process.");
    }

    /// <summary>Checks that a collector connects to the parent process that launched it.</summary>
    public static void VerifyServerProcess(NamedPipeClientStream pipe, int expectedProcessId)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId) || processId != expectedProcessId)
            throw new UnauthorizedAccessException("Unexpected hardware-helper parent process.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}

/// <summary>Authenticates protocol compatibility and the actual privilege mode of a connected helper.</summary>
public sealed record HardwareCollectorHello(int Version, bool Elevated);

/// <summary>Allows only telemetry collection or orderly shutdown; no hardware controls are exposed.</summary>
public sealed record HardwareCollectorRequest(int Version, string Command, [property: JsonRequired] string SamplingProfile);
