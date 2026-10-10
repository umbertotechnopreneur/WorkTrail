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


using Microsoft.Extensions.Logging;

namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    /// <inheritdoc />
    /// <param name="request">The existing VIP capture and optional plain-text note.</param>
    /// <param name="cancellationToken">Cancels the serialized note update.</param>
    public Task<OperationResult<bool>> SaveVipScreenshotNoteAsync(VipScreenshotNoteRequest request, CancellationToken cancellationToken) =>
        MutateAsync(() =>
        {
            if (!Guid.TryParseExact(request.CaptureId, "N", out _) || request.Note is null || request.Note.Length > 4000)
            {
                return Task.FromResult(OperationResult<bool>.Failure("snapshot.vip.note.invalid", "Snapshot.Vip.NoteInvalid"));
            }
            try
            {
                var saved = _store.SaveVipScreenshotNote(request with { Note = request.Note.Trim() });
                return Task.FromResult(saved
                    ? OperationResult<bool>.Success("snapshot.vip.note.saved", "Snapshot.Vip.NoteSaved", true)
                    : OperationResult<bool>.Failure("snapshot.vip.note.missing", "Snapshot.Vip.NoteUnavailable"));
            }
            catch (Exception exception)
            {
                // Never log the private note; the caller retains its editor contents for a retry.
                _logger.LogError(exception, "VIP screenshot note persistence failed. CaptureId={CaptureId}", request.CaptureId);
                return Task.FromResult(OperationResult<bool>.Failure("snapshot.vip.note.failed", "Snapshot.Vip.NoteFailed"));
            }
        }, cancellationToken);

    /// <inheritdoc />
    /// <param name="request">The bounded inclusive range of local dates.</param>
    /// <param name="cancellationToken">Cancels database and gallery reads.</param>
    public async Task<OperationResult<IReadOnlyList<DateOnly>>> GetVipScreenshotDatesAsync(
        VipScreenshotDatesRequest request, CancellationToken cancellationToken)
    {
        if (request.To < request.From || request.To.DayNumber - request.From.DayNumber > 366 || request.To == DateOnly.MaxValue)
        {
            return OperationResult<IReadOnlyList<DateOnly>>.Failure("snapshot.vip.dates.invalid", "ActivityCalendar.InvalidData");
        }
        try
        {
            var dates = await Task.Run(() => _store.GetVipScreenshotDates(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<DateOnly>>.Success("snapshot.vip.dates.loaded", "ScreenshotGalleryLoaded", dates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "VIP screenshot calendar projection failed.");
            return OperationResult<IReadOnlyList<DateOnly>>.Failure("snapshot.vip.dates.failed", "ActivityCalendar.Unavailable");
        }
    }
}
