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

public sealed partial class WorkTrailApplication
{
    /// <inheritdoc />
    public Task<OperationResult<DataArchiveProgress?>> GetDataArchiveProgressAsync(DataArchiveProgressRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.OperationId == Guid.Empty) throw new ArgumentException("An archive operation identity is required.", nameof(request));
        // No mutation gate: reads must stay responsive while the archive holds that gate.
        return Task.FromResult(OperationResult<DataArchiveProgress?>.Success("archive.progress", "", _archiveProgress.Read(request.OperationId)));
    }
}
