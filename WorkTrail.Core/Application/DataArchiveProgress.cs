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


using System.Collections.Concurrent;

namespace WorkTrail.Application;

/// <summary>Names real archive phases without exposing local paths or archive contents.</summary>
public enum DataArchivePhase
{
    PreparingDatabase, ScanningScreenshots, WritingArchive, VerifyingArchive,
    CheckingData, CopyingScreenshots, MergingData, PublishingScreenshots, RebuildingIndex, Finalizing
}

/// <summary>Reports processed items in the current phase, not an estimated overall percentage.</summary>
public sealed record DataArchiveProgress(DataArchivePhase Phase, long CompletedItems = 0, long? TotalItems = null);

/// <summary>Identifies an archive request whose progress can be read without its mutation lock.</summary>
public sealed record DataArchiveProgressRequest(Guid OperationId);

/// <summary>Keeps progress only for running operations; disposing a session removes its snapshot.</summary>
internal sealed class DataArchiveProgressRegistry
{
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    internal Session Begin(Guid id)
    {
        var session = new Session(this, id);
        if (id != Guid.Empty && !_sessions.TryAdd(id, session))
            throw new InvalidOperationException("An archive operation with this identity is already running.");
        return session;
    }

    internal DataArchiveProgress? Read(Guid id) => _sessions.TryGetValue(id, out var session) ? session.Value : null;

    internal sealed class Session(DataArchiveProgressRegistry owner, Guid id) : IDisposable
    {
        private DataArchiveProgress? _value;
        internal DataArchiveProgress? Value => Volatile.Read(ref _value);

        internal void Report(DataArchiveProgress value)
        {
            if (value.CompletedItems < 0 || value.TotalItems < value.CompletedItems)
                throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _value, value);
        }

        /// <summary>Releases progress without retaining archive history or local paths.</summary>
        public void Dispose()
        {
            if (id != Guid.Empty) owner._sessions.TryRemove(new KeyValuePair<Guid, Session>(id, this));
        }
    }
}
