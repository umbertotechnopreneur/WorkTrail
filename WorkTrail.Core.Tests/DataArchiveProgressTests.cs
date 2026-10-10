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
using System.Threading.Tasks;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class DataArchiveProgressTests
{
    /// <summary>Progress is isolated per operation, rejects duplicate identities and is removed on failure or completion.</summary>
    [Fact]
    public void Sessions_AreIsolatedAndRemovedOnDispose()
    {
        var registry = new DataArchiveProgressRegistry();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        using (var first = registry.Begin(firstId))
        using (var second = registry.Begin(secondId))
        {
            first.Report(new(DataArchivePhase.WritingArchive, 2, 5));
            second.Report(new(DataArchivePhase.RebuildingIndex));
            Assert.Equal(2, registry.Read(firstId)!.CompletedItems);
            Assert.Equal(DataArchivePhase.RebuildingIndex, registry.Read(secondId)!.Phase);
            Assert.Null(registry.Read(Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(() => registry.Begin(firstId));
            Assert.Throws<ArgumentOutOfRangeException>(() => first.Report(new(DataArchivePhase.WritingArchive, 6, 5)));
        }
        Assert.Null(registry.Read(firstId));
        Assert.Null(registry.Read(secondId));
    }

    /// <summary>Observers can read progress while the archive's worker is still processing.</summary>
    [Fact]
    public async Task Progress_IsReadableBeforeOperationCompletes()
    {
        var registry = new DataArchiveProgressRegistry();
        var id = Guid.NewGuid();
        using var session = registry.Begin(id);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            session.Report(new(DataArchivePhase.CopyingScreenshots, 1, 3));
            published.SetResult();
            await release.Task;
        });
        try
        {
            await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(worker.IsCompleted);
            Assert.Equal(new DataArchiveProgress(DataArchivePhase.CopyingScreenshots, 1, 3), registry.Read(id));
        }
        finally
        {
            release.TrySetResult();
            await worker;
        }
    }
}
