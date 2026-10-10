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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Cli;
using WorkTrail.Search;
using Xunit;

namespace WorkTrail.Cli.Tests;

/// <summary>Isolates console redirection from every other test collection.</summary>
[CollectionDefinition("CLI operational console", DisableParallelization = true)]
public sealed class CliOperationalConsoleCollection { }

/// <summary>Checks operational commands, consent boundaries and the Premium-only CLI contract.</summary>
[Collection("CLI operational console")]
public sealed class CliOperationsTests
{
    private const string Plan = "12345678-1234-1234-1234-123456789abc";
    private const string ImagePath = @"C:\Synthetic\shot.webp";
    private static readonly DateOnly Date = new(2026, 9, 21);

    /// <summary>Free cannot invoke any CLI entry point, including destructive commands with confirmation.</summary>
    [Theory]
    [InlineData()]
    [InlineData("help")]
    [InlineData("--version")]
    [InlineData("status")]
    [InlineData("access", "status")]
    [InlineData("--start")]
    [InlineData("reset", "run", "--yes", "--confirm", "DELETE-ALL-DATA")]
    public async Task Free_DeniesEveryCliEntryBeforeDispatch(params string[] args)
    {
        var (router, spy) = Create();
        spy.Access = OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new(ProductTier.Free, false, false));

        Assert.Equal(11, await router.RunAsync(args, CancellationToken.None));
        Assert.Empty(spy.Calls);
        Assert.Equal(1, spy.AccessReads);
        Assert.Equal(ProductTier.Premium, FeatureCatalog.Get(ProductFeature.Cli).RequiredTier);
    }

    /// <summary>A failed entitlement lookup must not dispatch the requested command.</summary>
    [Fact]
    public async Task MissingAccess_FailsClosedInsteadOfRunningTheCommand()
    {
        var (router, spy) = Create();
        spy.Access = OperationResult<FeatureAccessSnapshot>.Failure("runtime.unavailable", "unavailable");
        Assert.Equal(4, await router.RunAsync(["help"], CancellationToken.None));
        Assert.Empty(spy.Calls);
    }

    /// <summary>An unsupported tier must not grant CLI access.</summary>
    [Fact]
    public async Task InvalidAccessSnapshot_FailsClosed()
    {
        var (router, spy) = Create();
        spy.Access = OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new((ProductTier)42, false, false));
        Assert.Equal(8, await router.RunAsync(["status"], CancellationToken.None));
        Assert.Empty(spy.Calls);
    }

    /// <summary>A previously allowed router must observe subsequent entitlement loss.</summary>
    [Fact]
    public async Task AccessIsRecheckedOnEachInvocation()
    {
        var (router, spy) = Create();
        Assert.Equal(0, await router.RunAsync(["version"], CancellationToken.None));
        spy.Access = OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new(ProductTier.Free, true, true));
        Assert.Equal(11, await router.RunAsync(["version"], CancellationToken.None));
        Assert.Equal(2, spy.AccessReads);
    }

    /// <summary>A watch must stop after downgrade without reading another dashboard snapshot.</summary>
    [Fact]
    public async Task Watch_DowngradeStopsBeforeAnotherDashboardRead()
    {
        var (router, spy) = Create();
        spy.Return(nameof(IWorkTrailApplication.GetDashboardAsync), new DashboardState("PAUSED", "Ready", 0, 0, 0, 0, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        spy.AccessSequence.Enqueue(spy.Access);
        spy.AccessSequence.Enqueue(OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new(ProductTier.Free, true, true)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal(11, await router.RunAsync(["status", "--watch", "--interval", "1"], deadline.Token));
        Assert.Single(spy.Calls);
        Assert.Equal(2, spy.AccessReads);
    }

    /// <summary>Reset JSON reports acceptance, not completed deletion or private runtime targets.</summary>
    [Fact]
    public async Task Json_ResetReportsAcceptanceWithoutLeakingTheRuntimePlan()
    {
        var (router, spy) = Create(format: CliFormat.Json);
        spy.Return(nameof(IWorkTrailApplication.PrepareAtomicResetAsync), new AtomicResetPlan(@"C:\Synthetic\WorkTrail", @"C:\Synthetic\shots", @"C:\Synthetic\WorkTrail.exe"));
        var (exit, json) = await CaptureAsync(router, ["reset", "run", "--yes", "--confirm", "DELETE-ALL-DATA"]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, exit);
        Assert.Equal("app.reset.accepted", document.RootElement.GetProperty("code").GetString());
        Assert.False(document.RootElement.GetProperty("value").GetProperty("completionVerified").GetBoolean());
        Assert.DoesNotContain("Synthetic", json, StringComparison.Ordinal);
    }

    /// <summary>Quiet JSON mode still emits one machine-readable Premium denial.</summary>
    [Fact]
    public async Task Json_FreeReturnsOneStableDenialEvenWhenQuiet()
    {
        var (router, spy) = Create(format: CliFormat.Json);
        spy.Access = OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new(ProductTier.Free, false, false));
        var (exit, json) = await CaptureAsync(router, ["--help"]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(11, exit);
        Assert.False(document.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.Equal("cli.premium.required", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(spy.Calls);
    }

    /// <summary>Export preview discloses replacement behavior without writing an archive.</summary>
    [Fact]
    public async Task Json_ExportPreviewExplainsOverwriteWithoutCallingExport()
    {
        var (router, spy) = Create(format: CliFormat.Json);
        var (exit, json) = await CaptureAsync(router, ["data", "export", "--destination", @"C:\Synthetic\data.tmuarchive"]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, exit);
        var value = document.RootElement.GetProperty("value");
        Assert.True(value.GetProperty("replacesExistingDestination").GetBoolean());
        Assert.False(value.GetProperty("filesystemValidated").GetBoolean());
        Assert.Empty(spy.Calls);
    }

    /// <summary>Clock output contains civil-time data without astronomical or decorative fields.</summary>
    [Fact]
    public async Task Json_WorldClocksExcludeCelestialAndAssetFields()
    {
        var (router, spy) = Create(format: CliFormat.Json);
        var instant = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        spy.Return(nameof(IWorkTrailApplication.GetWorldClocksAsync), new WorldClockSnapshot(instant,
            [new WorldClockItem("city", "City", "IT", "UTC", instant, false, null, true, null, null, 0, "private.png", "summer", null!, null)],
            8, null!, null!));
        var (exit, json) = await CaptureAsync(router, ["world-clock", "list"]);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, exit);
        var clock = document.RootElement.GetProperty("value").GetProperty("clocks")[0];
        Assert.Equal("UTC", clock.GetProperty("timeZoneId").GetString());
        Assert.False(clock.TryGetProperty("sunrise", out _));
        Assert.DoesNotContain("private.png", json, StringComparison.Ordinal);
        Assert.False(document.RootElement.GetProperty("value").TryGetProperty("map", out _));
    }

    /// <summary>Operational reads use the intended facade and preserve its failure code.</summary>
    [Theory]
    [InlineData("search", "status", nameof(IWorkTrailApplication.GetSearchAvailabilityAsync))]
    [InlineData("hardware", "snapshot", nameof(IWorkTrailApplication.CaptureHardwareSnapshotAsync))]
    [InlineData("world-clock", "list", nameof(IWorkTrailApplication.GetWorldClocksAsync))]
    [InlineData("world-clock", "cities", nameof(IWorkTrailApplication.GetWorldClockCityCatalogAsync))]
    [InlineData("ai", "models", nameof(IWorkTrailApplication.GetAiModelCatalogAsync))]
    [InlineData("ai", "pricing", nameof(IWorkTrailApplication.GetAiPricingOverviewAsync))]
    [InlineData("logs", "open", nameof(IWorkTrailApplication.OpenApplicationLogAsync))]
    [InlineData("logs", "open-folder", nameof(IWorkTrailApplication.OpenApplicationLogFolderAsync))]
    [InlineData("screenshots", "gallery", nameof(IWorkTrailApplication.GetLatestScreenshotGalleryAsync))]
    public async Task OperationalCommands_RouteToTheirFacadeAndPreserveFailure(string root, string action, string method)
    {
        var (router, spy) = Create();
        spy.ExpectFailure(method);
        Assert.Equal(4, await router.RunAsync([root, action], CancellationToken.None));
        Assert.Equal(method, Assert.Single(spy.Calls).Method);
    }

    /// <summary>Invalid syntax and unsupported values fail before any operational call.</summary>
    [Theory]
    [InlineData("report", "--from", "21/09/2026", "--to", "2026-09-22", "--timezone", "UTC")]
    [InlineData("report", "--from", "2026-09-22", "--to", "2026-09-21", "--timezone", "UTC")]
    [InlineData("report", "--from", "2026-09-21", "--to", "2026-09-22", "--timezone", "UTC", "--view", "42")]
    [InlineData("search", "query")]
    [InlineData("search", "query", "--text", "x", "--limit", "0")]
    [InlineData("search", "query", "--text", "x", "--offset", "-1")]
    [InlineData("search", "query", "--text", "x", "--from", "2026-09-21T12:30:00")]
    [InlineData("search", "query", "--text", "x", "--from", "2026-09-22T00:00:00Z", "--to", "2026-09-21T00:00:00Z")]
    [InlineData("search", "query", "--text", "x", "--text", "y")]
    [InlineData("screenshots", "gallery", "--date", "2026-02-30")]
    [InlineData("screenshots", "delete", "--date", "2026-09-21", "--path", "relative.webp", "--yes")]
    [InlineData("data", "export", "--destination", "relative.tmuarchive", "--yes")]
    [InlineData("data", "export", "--destination", "C:\\Synthetic\\data.tmuarchive", "--from", "2026-09-21")]
    [InlineData("data", "import", "run", "--plan", "00000000-0000-0000-0000-000000000000", "--yes")]
    [InlineData("ai", "reprocess", "status", "--job", "bad-id")]
    [InlineData("world-clock", "convert", "--city", "rome", "--local-time", "2026-09-21T12:00:00Z")]
    public async Task InvalidInput_DoesNotReachOperationalServices(params string[] args)
    {
        var (router, spy) = Create();
        Assert.Equal(2, await router.RunAsync(args, CancellationToken.None));
        Assert.Empty(spy.Calls);
    }

    /// <summary>Search preserves explicit offsets, pagination and opt-in text across cultures.</summary>
    [Theory]
    [InlineData("it-IT", "2026-09-21T12:30:00+07:00", false)]
    [InlineData("en-US", "2026-09-21T05:30:00Z", true)]
    [InlineData("fr-FR", "2026-09-21T05:30:00.125Z", false)]
    public async Task Search_ParsesInvariantBoundsAndMakesTextBodiesOptIn(string culture, string timestamp, bool includeText)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var (router, spy) = Create();
            spy.ExpectFailure(nameof(IWorkTrailApplication.SearchAsync));
            var args = new List<string> { "search", "query", "--text", "project", "--from", timestamp, "--limit", "10", "--offset", "5", "--kind", "screenshot" };
            if (includeText) args.Add("--include-text");
            Assert.Equal(4, await router.RunAsync(args, CancellationToken.None));
            var request = Assert.IsType<SearchRequest>(Assert.Single(spy.Calls).Arguments[0]);
            Assert.Equal(10, request.Limit);
            Assert.Equal(5, request.Offset);
            Assert.Equal(includeText, request.IncludeTextContent);
            Assert.Equal(5, request.FromInclusive!.Value.UtcDateTime.Hour);
            Assert.Contains("screenshot", request.Kinds);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>Reports and civil-time conversions preserve their distinct time-zone contracts.</summary>
    [Fact]
    public async Task ReportAndClock_PreserveExplicitTimeSemantics()
    {
        var (router, spy) = Create();
        spy.ExpectFailure(nameof(IWorkTrailApplication.GetReportAsync));
        Assert.Equal(4, await router.RunAsync(["report", "--from", "2026-09-21", "--to", "2026-09-22", "--timezone", "UTC", "--view", "hour-of-week"], CancellationToken.None));
        var query = Assert.IsType<ReportQuery>(Assert.Single(spy.Calls).Arguments[0]);
        Assert.Equal(new ReportQuery(Date, Date.AddDays(1), "UTC", ReportView.HourOfWeek), query);
        spy.Calls.Clear();
        spy.ExpectFailure(nameof(IWorkTrailApplication.ConvertWorldClocksAsync));
        Assert.Equal(4, await router.RunAsync(["world-clock", "convert", "--city", "rome", "--local-time", "2026-09-21T12:30:00"], CancellationToken.None));
        var conversion = Assert.IsType<WorldClockConversionRequest>(Assert.Single(spy.Calls).Arguments[0]);
        Assert.Equal(DateTimeKind.Unspecified, conversion.ReferenceLocalTime.Kind);
        Assert.Equal("rome", conversion.ReferenceCityId);
    }

    /// <summary>Deletion resolves the retained item before preview or confirmed mutation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScreenshotDelete_ResolvesExactGalleryItemBeforeAnyDeletion(bool confirmed)
    {
        var (router, spy) = Create();
        spy.Return(nameof(IWorkTrailApplication.GetScreenshotGalleryAsync), Gallery(ImagePath));
        spy.Return(nameof(IWorkTrailApplication.DeleteScreenshotAsync), ImagePath);
        string[] args = ["screenshots", "delete", "--date", "2026-09-21", "--path", ImagePath];
        if (confirmed) args = [.. args, "--yes"];
        Assert.Equal(0, await router.RunAsync(args, CancellationToken.None));
        Assert.Equal(confirmed ? 2 : 1, spy.Calls.Count);
        Assert.Equal(nameof(IWorkTrailApplication.GetScreenshotGalleryAsync), spy.Calls[0].Method);
        if (confirmed) Assert.Equal(ImagePath, spy.Calls[1].Arguments[0]);
    }

    /// <summary>An unrelated path must never reach the screenshot deletion service.</summary>
    [Fact]
    public async Task ScreenshotDelete_RefusesAPathNotInTheRequestedGallery()
    {
        var (router, spy) = Create();
        spy.Return(nameof(IWorkTrailApplication.GetScreenshotGalleryAsync), Gallery(@"C:\Synthetic\other.webp"));
        Assert.Equal(10, await router.RunAsync(["screenshots", "delete", "--date", "2026-09-21", "--path", ImagePath, "--yes"], CancellationToken.None));
        Assert.Single(spy.Calls);
    }

    /// <summary>Export forwards the selected range only after confirmation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_PreviewsUnlessConfirmedAndPreservesRange(bool confirmed)
    {
        var (router, spy) = Create();
        spy.ExpectFailure(nameof(IWorkTrailApplication.ExportDataArchiveAsync));
        string[] args = ["data", "export", "--destination", @"C:\Synthetic\data.tmuarchive", "--from", "2026-09-21", "--to", "2026-09-22", "--no-screenshots"];
        if (confirmed) args = [.. args, "--yes"];
        Assert.Equal(confirmed ? 4 : 0, await router.RunAsync(args, CancellationToken.None));
        if (!confirmed) { Assert.Empty(spy.Calls); return; }
        var request = Assert.IsType<DataArchiveExportRequest>(Assert.Single(spy.Calls).Arguments[0]);
        Assert.Equal(Date, request.From);
        Assert.Equal(Date.AddDays(1), request.ToInclusive);
        Assert.False(request.IncludeScreenshots);
    }

    /// <summary>Potentially destructive or provider-backed actions require their exact confirmations.</summary>
    [Theory]
    [InlineData("ai", "test")]
    [InlineData("data", "import", "run", "--plan", Plan)]
    [InlineData("ai", "reprocess", "start", "--plan", Plan)]
    [InlineData("ai", "reprocess", "resume", "--job", Plan)]
    [InlineData("reset", "run")]
    [InlineData("reset", "run", "--yes")]
    [InlineData("reset", "run", "--confirm", "DELETE-ALL-DATA")]
    [InlineData("reset", "run", "--yes", "--confirm", "delete-all-data")]
    public async Task RequiredConfirmation_CannotBeInferred(params string[] args)
    {
        var (router, spy) = Create();
        Assert.Equal(3, await router.RunAsync(args, CancellationToken.None));
        Assert.Empty(spy.Calls);
    }

    /// <summary>Global consent does not replace the second reset confirmation.</summary>
    [Fact]
    public async Task GlobalYes_IsAcceptedButCannotReplaceTheResetPhrase()
    {
        var (router, spy) = Create(globalYes: true);
        Assert.Equal(3, await router.RunAsync(["reset", "run"], CancellationToken.None));
        Assert.Empty(spy.Calls);
        spy.Return(nameof(IWorkTrailApplication.PrepareAtomicResetAsync), new AtomicResetPlan(@"C:\Synthetic\WorkTrail", @"C:\Synthetic\shots", @"C:\Synthetic\WorkTrail.exe"));
        Assert.Equal(0, await router.RunAsync(["reset", "run", "--confirm", "DELETE-ALL-DATA"], CancellationToken.None));
        Assert.Equal(new AtomicResetRequest(true, true), Assert.Single(spy.Calls).Arguments[0]);
    }

    /// <summary>Historical processing forwards the reviewed runtime plan or job identity.</summary>
    [Theory]
    [InlineData("start", "--plan", true, nameof(IWorkTrailApplication.StartAiScreenshotReprocessingAsync))]
    [InlineData("status", "--job", false, nameof(IWorkTrailApplication.GetAiScreenshotReprocessingJobAsync))]
    [InlineData("pause", "--job", false, nameof(IWorkTrailApplication.PauseAiScreenshotReprocessingAsync))]
    [InlineData("resume", "--job", true, nameof(IWorkTrailApplication.ResumeAiScreenshotReprocessingAsync))]
    public async Task Reprocess_UsesRuntimeOwnedIds(string action, string option, bool yes, string method)
    {
        var (router, spy) = Create();
        spy.ExpectFailure(method);
        string[] args = ["ai", "reprocess", action, option, Plan];
        if (yes) args = [.. args, "--yes"];
        Assert.Equal(4, await router.RunAsync(args, CancellationToken.None));
        Assert.Equal(Guid.Parse(Plan), Assert.Single(spy.Calls).Arguments[0]);
    }

    /// <summary>Import must use the confirmed plan without silently replacing its preview.</summary>
    [Fact]
    public async Task Import_UsesConfirmedPlanWithoutCreatingAnotherPreview()
    {
        var (router, spy) = Create();
        spy.ExpectFailure(nameof(IWorkTrailApplication.ImportDataArchiveAsync));
        Assert.Equal(4, await router.RunAsync(["data", "import", "run", "--plan", Plan, "--yes"], CancellationToken.None));
        Assert.Equal(new DataArchiveImportRequest(Guid.Parse(Plan)), Assert.Single(spy.Calls).Arguments[0]);
    }

    /// <summary>Historical preview must not queue provider-backed processing.</summary>
    [Fact]
    public async Task ReprocessingPreview_DoesNotStartProviderCalls()
    {
        var (router, spy) = Create();
        spy.ExpectFailure(nameof(IWorkTrailApplication.PreviewAiScreenshotReprocessingAsync));
        Assert.Equal(4, await router.RunAsync(["ai", "reprocess", "preview", "--date", "2026-09-21"], CancellationToken.None));
        Assert.Equal(new AiScreenshotReprocessRequest(Date), Assert.Single(spy.Calls).Arguments[0]);
    }

    private static ScreenshotGallery Gallery(string path) => new(Date,
        [new ScreenshotGalleryItem(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero), path, "Synthetic", "active-window", "manual")]);

    private static async Task<(int Exit, string Json)> CaptureAsync(CliRouter router, string[] args)
    {
        var previous = Console.Out;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(writer);
            var exit = await router.RunAsync(args, CancellationToken.None);
            return (exit, writer.ToString());
        }
        finally { Console.SetOut(previous); }
    }

    private static (CliRouter Router, ApplicationSpy Spy) Create(bool globalYes = false, CliFormat format = CliFormat.Plain)
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, ApplicationSpy>();
        var spy = (ApplicationSpy)application;
        var options = new CliOptions(format, "en-US", true, globalYes, 5, false, []);
        return (new CliRouter(application, new CliOutput(options), options), spy);
    }

    /// <summary>Fails tests on unexpected facade calls, including accidental I/O-capable operations.</summary>
    public class ApplicationSpy : DispatchProxy
    {
        internal OperationResult<FeatureAccessSnapshot> Access { get; set; } =
            OperationResult<FeatureAccessSnapshot>.Success("access.loaded", "loaded", new(ProductTier.Premium, false, false));
        internal int AccessReads { get; private set; }
        internal Queue<OperationResult<FeatureAccessSnapshot>> AccessSequence { get; } = [];
        internal List<(string Method, object?[] Arguments)> Calls { get; } = [];
        private readonly Dictionary<string, object> _responses = [];

        internal void Return<T>(string method, T value) =>
            _responses[method] = Task.FromResult(OperationResult<T>.Success("test.loaded", "loaded", value));

        internal void ExpectFailure(string method)
        {
            var resultType = typeof(IWorkTrailApplication).GetMethod(method)!.ReturnType.GetGenericArguments()[0].GetGenericArguments()[0];
            _responses[method] = typeof(ApplicationSpy).GetMethod(nameof(Failure), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(resultType).Invoke(null, null)!;
        }

        private static object Failure<T>() => Task.FromResult(OperationResult<T>.Failure("runtime.unavailable", "unavailable"));

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!.Name;
            if (method == nameof(IWorkTrailApplication.GetFeatureAccessAsync))
            {
                AccessReads++;
                return Task.FromResult(AccessSequence.Count > 0 ? AccessSequence.Dequeue() : Access);
            }
            Calls.Add((method, args ?? []));
            Assert.True(_responses.ContainsKey(method), $"Unexpected application call: {method}");
            return _responses[method];
        }
    }
}
