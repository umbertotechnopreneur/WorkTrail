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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkTrail.Application;

namespace WorkTrail.Runtime;

/// <summary>Defines the versioned, length-prefixed local runtime protocol.</summary>
public static class RuntimeProtocol
{
    /// <summary>Gets the supported wire-protocol version.</summary>
    public const int ProtocolVersion = 5;

    /// <summary>Gets the maximum accepted JSON envelope size in bytes.</summary>
    public const int MaximumMessageBytes = 16_777_216;

    /// <summary>Builds the mutex and named-pipe names from an installation identifier without exposing it.</summary>
    public static RuntimeEndpoint CreateEndpoint(string installationId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installationId ?? string.Empty))).ToLowerInvariant()[..32];
        return new RuntimeEndpoint($"Local\\WorkTrail.Runtime.{hash}", $"WorkTrail.Runtime.{hash}");
    }

    /// <summary>Writes a UTF-8 JSON envelope preceded by its 32-bit little-endian length.</summary>
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        if (payload.Length > MaximumMessageBytes)
        {
            throw new InvalidOperationException("IPC message exceeds the protocol limit.");
        }

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one validated length-prefixed JSON envelope.</summary>
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > MaximumMessageBytes)
        {
            throw new InvalidOperationException("IPC message length is invalid.");
        }

        var payload = new byte[count];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, SerializerOptions) ?? throw new InvalidOperationException("IPC message payload is invalid.");
    }

    /// <summary>Gets the shared JSON options used for local protocol envelopes.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var consumed = 0;
        while (consumed < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[consumed..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The runtime closed the pipe before its message completed.");
            }

            consumed += read;
        }
    }
}

/// <summary>Contains the obfuscated local kernel-object names for one installation.</summary>
public sealed record RuntimeEndpoint(string MutexName, string PipeName);

/// <summary>Represents one request submitted to the local runtime.</summary>
public sealed record RuntimeRequestEnvelope(
    int ProtocolVersion,
    Guid RequestId,
    string Operation,
    JsonElement Payload,
    string? Locale,
    string? ClientVersion);

/// <summary>Represents the result returned by the local runtime.</summary>
public sealed record RuntimeResponseEnvelope(
    int ProtocolVersion,
    Guid RequestId,
    bool Succeeded,
    string Code,
    string MessageKey,
    object? Payload,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>Carries one validated world-clock reorder request over the same-user runtime protocol.</summary>
internal sealed record WorldClockMoveRequest(string CityId, WorldClockMoveDirection Direction);
