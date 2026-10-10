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


using System.Globalization;
using WorkTrail.Application;
using WorkTrail.Search;

namespace WorkTrail.Cli;

public sealed partial class CliRouter
{
    private async Task<int?> CheckCliAccessAsync(CancellationToken cancellationToken)
    {
        // Always ask the shared runtime: settings, arguments and stale shell state cannot grant CLI access.
        var access = await _application.GetFeatureAccessAsync(cancellationToken);
        if (!access.Succeeded) return WriteResult(access);
        if (access.Value is null || !Enum.IsDefined(access.Value.Tier))
        {
            return WriteResult(OperationResult<object>.Failure("cli.access.failed", "CliAccessUnavailable"));
        }
        return FeatureCatalog.IsAllowed(ProductFeature.Cli, access.Value)
            ? null
            : WriteResult(OperationResult<object>.Failure("cli.premium.required", "CliPremiumRequired"));
    }

    private async Task<int> SearchAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.Count == 2 && arguments[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return await WriteAsync(_application.GetSearchAvailabilityAsync(cancellationToken));
        }

        if (arguments.ElementAtOrDefault(1)?.Equals("query", StringComparison.OrdinalIgnoreCase) != true
            || !TryParseOptions(arguments, 2, ["--include-text"],
                ["--text", "--kind", "--from", "--to", "--limit", "--offset", "--query-language"], out var parsed)
            || !TryOptionalInstant(parsed.Value("--from"), out var from)
            || !TryOptionalInstant(parsed.Value("--to"), out var to)
            || from >= to
            || !TryOptionalNumber(parsed.Value("--limit"), 1, out var limit)
            || !TryOptionalNumber(parsed.Value("--offset"), 0, out var offset))
        {
            return InvalidArguments();
        }

        var text = parsed.Value("--text") ?? string.Empty;
        var kind = parsed.Value("--kind");
        if (text.Length == 0 && kind is null && from is null && to is null)
        {
            return InvalidArguments("text");
        }

        return await WriteAsync(_application.SearchAsync(new SearchRequest
        {
            Text = text,
            Kinds = kind is null ? [] : [kind],
            FromInclusive = from,
            ToExclusive = to,
            Limit = limit,
            Offset = offset ?? 0,
            QueryLanguage = parsed.Value("--query-language"),
            IncludeTextContent = parsed.Contains("--include-text")
        }, cancellationToken));
    }

    private async Task<int> ReportAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!TryParseOptions(arguments, 1, [], ["--from", "--to", "--timezone", "--view"], out var parsed)
            || !TryDate(parsed.Value("--from"), out var from)
            || !TryDate(parsed.Value("--to"), out var to)
            || from > to
            || parsed.Value("--timezone") is not { } timeZone)
        {
            return InvalidArguments();
        }

        ReportView? view = parsed.Value("--view")?.ToLowerInvariant() switch
        {
            null or "calendar" => ReportView.Calendar,
            "hour-of-week" => ReportView.HourOfWeek,
            "trend" => ReportView.Trend,
            "applications" => ReportView.Applications,
            _ => null
        };
        return view is null ? InvalidArguments("view")
            : await WriteAsync(_application.GetReportAsync(new ReportQuery(from, to, timeZone, view.Value), cancellationToken));
    }

    private async Task<int> WorldClockAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.Count == 2 && arguments[1].Equals("cities", StringComparison.OrdinalIgnoreCase))
        {
            return await WriteAsync(_application.GetWorldClockCityCatalogAsync(cancellationToken));
        }

        OperationResult<WorldClockSnapshot> result;
        if (arguments.Count == 2 && arguments[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            result = await _application.GetWorldClocksAsync(cancellationToken);
        }
        else if (arguments.ElementAtOrDefault(1)?.Equals("convert", StringComparison.OrdinalIgnoreCase) == true
            && TryParseOptions(arguments, 2, [], ["--city", "--local-time"], out var parsed)
            && parsed.Value("--city") is { } city
            && DateTime.TryParseExact(parsed.Value("--local-time"), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime))
        {
            result = await _application.ConvertWorldClocksAsync(new WorldClockConversionRequest(city, localTime), cancellationToken);
        }
        else
        {
            return InvalidArguments();
        }

        // Only clock data is projected; decorative assets and celestial maps have no terminal role.
        if (!result.Succeeded || result.Value is null) return WriteResult(result);
        var value = new
        {
            result.Value.InstantUtc,
            clocks = result.Value.Clocks.Select(clock => new
            {
                clock.CityId,
                clock.CityName,
                clock.CountryCode,
                clock.TimeZoneId,
                clock.LocalTime,
                clock.IsDaylightSavingTime,
                clock.DaylightSavingEndsAt
            }).ToArray()
        };
        return WriteResult(new OperationResult<object>(true, result.Code, result.MessageKey, value, result.Issues));
    }

    private async Task<int> ScreenshotMaintenanceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        switch (arguments[1].ToLowerInvariant())
        {
            case "gallery":
                if (!TryParseOptions(arguments, 2, [], ["--date"], out var gallery)) return InvalidArguments();
                if (gallery.Value("--date") is null) return await WriteAsync(_application.GetLatestScreenshotGalleryAsync(cancellationToken));
                return TryDate(gallery.Value("--date"), out var date)
                    ? await WriteAsync(_application.GetScreenshotGalleryAsync(date, cancellationToken)) : InvalidArguments("date");
            case "delete":
                if (!TryParseOptions(arguments, 2, ["--yes"], ["--date", "--path"], out var deletion)
                    || !TryDate(deletion.Value("--date"), out var deleteDate)
                    || !IsAbsolutePath(deletion.Value("--path"))) return InvalidArguments();

                // Resolve the exact retained item through Core before previewing or deleting it.
                var retained = await _application.GetScreenshotGalleryAsync(deleteDate, cancellationToken);
                if (!retained.Succeeded || retained.Value is null) return WriteResult(retained);
                var item = retained.Value.Items.FirstOrDefault(candidate =>
                    candidate.Path.Equals(deletion.Value("--path"), StringComparison.OrdinalIgnoreCase));
                if (item is null) return WriteResult(OperationResult<object>.Failure("screenshot.not_found", "ScreenshotNotFound"));
                if (!Confirmed(deletion))
                {
                    return Preview("screenshot.delete.previewed", new
                    {
                        item.Path,
                        item.CapturedAt,
                        deletesImage = true,
                        deletesAssociatedAnalysis = true,
                        confirmationRequired = "--yes"
                    });
                }
                return await WriteAsync(_application.DeleteScreenshotAsync(item.Path, cancellationToken));
            default:
                return InvalidArguments();
        }
    }

    private async Task<int> DataAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.ElementAtOrDefault(1)?.Equals("export", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!TryParseOptions(arguments, 2, ["--yes", "--no-screenshots"], ["--destination", "--from", "--to"], out var parsed)
                || !IsAbsolutePath(parsed.Value("--destination"))
                || !parsed.Value("--destination")!.EndsWith(".tmuarchive", StringComparison.OrdinalIgnoreCase)
                || !TryOptionalDate(parsed.Value("--from"), out var from)
                || !TryOptionalDate(parsed.Value("--to"), out var to)
                || (from is null) != (to is null) || from > to)
            {
                return InvalidArguments();
            }

            var request = new DataArchiveExportRequest(parsed.Value("--destination")!, from, to, !parsed.Contains("--no-screenshots"));
            // The preview describes the request only. Core validates the filesystem when export is confirmed.
            return !Confirmed(parsed)
                ? Preview("archive.export.request_previewed", new { request, confirmationRequired = "--yes", filesystemValidated = false, replacesExistingDestination = true })
                : await WriteAsync(_application.ExportDataArchiveAsync(request, cancellationToken));
        }

        if (arguments.ElementAtOrDefault(1)?.Equals("import", StringComparison.OrdinalIgnoreCase) != true) return InvalidArguments();
        if (arguments.ElementAtOrDefault(2)?.Equals("preview", StringComparison.OrdinalIgnoreCase) == true
            && TryParseOptions(arguments, 3, [], ["--path"], out var preview)
            && IsAbsolutePath(preview.Value("--path")))
        {
            return await WriteAsync(_application.PreviewDataArchiveImportAsync(new DataArchiveImportPreviewRequest(preview.Value("--path")!), cancellationToken));
        }

        if (arguments.ElementAtOrDefault(2)?.Equals("run", StringComparison.OrdinalIgnoreCase) == true
            && TryParseOptions(arguments, 3, ["--yes"], ["--plan"], out var run)
            && TryId(run.Value("--plan"), out var planId))
        {
            // The runtime owns expiry, fingerprint checks and the serialized merge; no CLI-side archive I/O.
            return Confirmed(run)
                ? await WriteAsync(_application.ImportDataArchiveAsync(new DataArchiveImportRequest(planId), cancellationToken))
                : ConfirmationRequired();
        }

        return InvalidArguments();
    }

    private async Task<int> AiReprocessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var action = arguments.ElementAtOrDefault(2)?.ToLowerInvariant();
        if (action == "preview" && TryParseOptions(arguments, 3, [], ["--date"], out var preview)
            && TryDate(preview.Value("--date"), out var date))
        {
            return await WriteAsync(_application.PreviewAiScreenshotReprocessingAsync(new AiScreenshotReprocessRequest(date), cancellationToken));
        }

        if (action == "start" && TryParseOptions(arguments, 3, ["--yes"], ["--plan"], out var start)
            && TryId(start.Value("--plan"), out var plan))
        {
            return Confirmed(start)
                ? await WriteAsync(_application.StartAiScreenshotReprocessingAsync(plan, cancellationToken))
                : ConfirmationRequired();
        }

        if (action is not ("status" or "pause" or "resume")
            || !TryParseOptions(arguments, 3, action == "resume" ? ["--yes"] : [], ["--job"], out var job)
            || !TryId(job.Value("--job"), out var jobId)) return InvalidArguments();

        return action switch
        {
            "status" => await WriteAsync(_application.GetAiScreenshotReprocessingJobAsync(jobId, cancellationToken)),
            "pause" => await WriteAsync(_application.PauseAiScreenshotReprocessingAsync(jobId, cancellationToken)),
            _ => Confirmed(job)
                ? await WriteAsync(_application.ResumeAiScreenshotReprocessingAsync(jobId, cancellationToken))
                : ConfirmationRequired()
        };
    }

    private async Task<int> LogsAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => arguments.Count == 2
        ? arguments[1].ToLowerInvariant() switch
        {
            "open" => await WriteAsync(_application.OpenApplicationLogAsync(cancellationToken)),
            "open-folder" => await WriteAsync(_application.OpenApplicationLogFolderAsync(cancellationToken)),
            _ => InvalidArguments()
        }
        : InvalidArguments();

    private async Task<int> ResetAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (arguments.Count == 2 && arguments[1].Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            var status = await _application.GetRetentionStatusAsync(cancellationToken);
            if (!status.Succeeded || status.Value is null) return WriteResult(status);
            return Preview("app.reset.scope_previewed", new
            {
                status.Value.ScreenshotDirectory,
                deletesAllLocalApplicationData = true,
                deletesOwnedScreenshots = true,
                disablesStartup = true,
                relaunchesApplication = true,
                confirmationRequired = "--yes --confirm DELETE-ALL-DATA"
            });
        }

        if (arguments.ElementAtOrDefault(1)?.Equals("run", StringComparison.OrdinalIgnoreCase) != true
            || !TryParseOptions(arguments, 2, ["--yes"], ["--confirm"], out var parsed)) return InvalidArguments();
        if (!Confirmed(parsed) || parsed.Value("--confirm") != "DELETE-ALL-DATA") return ConfirmationRequired();

        // The runtime responds before its owner shuts down and deletes data. Do not claim completed deletion.
        var result = await _application.PrepareAtomicResetAsync(new AtomicResetRequest(true, true), cancellationToken);
        return !result.Succeeded ? WriteResult(result)
            : WriteResult(OperationResult<object>.Success("app.reset.accepted", "ResetAccepted",
                new { accepted = true, completionVerified = false }));
    }

    private bool Confirmed(ParsedOptions parsed) => _options.Yes || parsed.Contains("--yes");

    private int ConfirmationRequired() => WriteResult(OperationResult<object>.Failure(
        "command.confirmation.required", "ConfirmationRequired",
        new ValidationIssue("confirmation", "required", "ConfirmationRequired")));

    private int Preview<T>(string code, T value) => WriteResult(OperationResult<T>.Success(code, "PreviewReady", value));

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryOptionalDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null) return true;
        if (!TryDate(value, out var parsed)) return false;
        date = parsed;
        return true;
    }

    private static bool TryOptionalInstant(string? value, out DateTimeOffset? instant)
    {
        instant = null;
        if (value is null) return true;
        // An explicit offset avoids depending on the CLI machine's local time zone for search bounds.
        string[] formats = ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];
        if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed)) return false;
        instant = parsed;
        return true;
    }

    private static bool TryOptionalNumber(string? value, int minimum, out int? number)
    {
        number = null;
        if (value is null) return true;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum) return false;
        number = parsed;
        return true;
    }

    private static bool TryId(string? value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;

    private static bool IsAbsolutePath(string? value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value);
}
