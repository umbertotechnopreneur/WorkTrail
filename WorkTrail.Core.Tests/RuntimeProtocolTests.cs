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
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Runtime;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class RuntimeProtocolTests
{
    [Fact]
    public void EndpointNames_AreStableAndDoNotExposeInstallationId()
    {
        var endpoint = RuntimeProtocol.CreateEndpoint("machine-private-installation-id");

        Assert.StartsWith("Local\\WorkTrail.Runtime.", endpoint.MutexName);
        Assert.StartsWith("WorkTrail.Runtime.", endpoint.PipeName);
        Assert.DoesNotContain("machine-private-installation-id", endpoint.MutexName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchOptions_StripsCliSwitchFromCommandArguments()
    {
        var options = LaunchOptions.Parse(["-cli", "--language", "it-IT", "status"]);

        Assert.Equal(LaunchMode.Cli, options.Mode);
        Assert.Equal("it-IT", options.Language);
        Assert.Equal(["status"], options.RemainingArguments);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("zh")]
    [InlineData("zh-Hant")]
    public void LaunchOptions_RejectsUnsupportedOrAmbiguousLanguageAliases(string language)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--language", language]));
    }

    [Fact]
    public void LaunchOptions_RequiresLanguageValue()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--language"]));
    }

    [Fact]
    public void LaunchOptions_RejectsRemovedReportsWindow()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["reports"]));
    }

    [Fact]
    public void LaunchOptions_WindowsStartupSwitchRequestsNotificationAreaLaunch()
    {
        var options = LaunchOptions.Parse(["--start-with-windows"]);

        Assert.True(options.StartWithWindows);
        Assert.Empty(options.RemainingArguments);
    }

    [Fact]
    public void LaunchOptions_ThemeOverrideDoesNotRestoreRemovedReportsWindow()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["reports", "--theme", "dark"]));
    }

    [Theory]
    [InlineData("-cli", "reports")]
    [InlineData("reports", "--cli")]
    public void LaunchOptions_CliSwitchTakesPrecedenceOverReportsVerb(string first, string second)
    {
        var options = LaunchOptions.Parse([first, second]);

        Assert.Equal(LaunchMode.Cli, options.Mode);
        Assert.Equal(["reports"], options.RemainingArguments);
    }

    [Fact]
    public async Task ReportQueryV1_RoundTripsThroughTheWireEnvelope()
    {
        var expected = new ReportQuery(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 28),
            "UTC",
            ReportView.HourOfWeek);
        var request = new RuntimeRequestEnvelope(
            RuntimeProtocol.ProtocolVersion,
            Guid.NewGuid(),
            "report.query.v1",
            JsonSerializer.SerializeToElement(expected, RuntimeProtocol.SerializerOptions),
            "it",
            "test");
        await using var stream = new MemoryStream();

        await RuntimeProtocol.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        var actualEnvelope = await RuntimeProtocol.ReadAsync<RuntimeRequestEnvelope>(stream, CancellationToken.None);
        var actual = actualEnvelope.Payload.Deserialize<ReportQuery>(RuntimeProtocol.SerializerOptions);

        Assert.Equal("report.query.v1", actualEnvelope.Operation);
        Assert.Equal(expected, actual);
    }

    /// <summary>Verifies that retained image bytes survive the local JSON protocol exactly.</summary>
    [Fact]
    public async Task ScreenshotImageV1_RoundTripsRequestAndContentBytes()
    {
        var requestPayload = new ScreenshotImageRequest(@"C:\retained\capture.webp");
        var request = new RuntimeRequestEnvelope(
            RuntimeProtocol.ProtocolVersion,
            Guid.NewGuid(),
            "screenshot.image.get.v1",
            JsonSerializer.SerializeToElement(requestPayload, RuntimeProtocol.SerializerOptions),
            "it-IT",
            "test");
        await using var requestStream = new MemoryStream();

        await RuntimeProtocol.WriteAsync(requestStream, request, CancellationToken.None);
        requestStream.Position = 0;
        var actualRequestEnvelope = await RuntimeProtocol.ReadAsync<RuntimeRequestEnvelope>(requestStream, CancellationToken.None);
        var actualRequest = actualRequestEnvelope.Payload.Deserialize<ScreenshotImageRequest>(RuntimeProtocol.SerializerOptions);

        Assert.Equal(requestPayload, actualRequest);

        var expectedContent = new ScreenshotImageContent("capture", [1, 4, 9, 16]);
        var response = new RuntimeResponseEnvelope(
            RuntimeProtocol.ProtocolVersion,
            request.RequestId,
            true,
            "screenshot.image.loaded",
            "ScreenshotImageLoaded",
            expectedContent,
            []);
        await using var responseStream = new MemoryStream();

        await RuntimeProtocol.WriteAsync(responseStream, response, CancellationToken.None);
        responseStream.Position = 0;
        var actualResponseEnvelope = await RuntimeProtocol.ReadAsync<RuntimeResponseEnvelope>(responseStream, CancellationToken.None);
        var payload = Assert.IsType<JsonElement>(actualResponseEnvelope.Payload);
        var actualContent = payload.Deserialize<ScreenshotImageContent>(RuntimeProtocol.SerializerOptions);

        Assert.NotNull(actualContent);
        Assert.Equal(expectedContent.ArtifactIdentity, actualContent.ArtifactIdentity);
        Assert.Equal(expectedContent.Content, actualContent.Content);
    }

    /// <summary>Verifies the typed client, host dispatcher, and application facade use the screenshot-image operation end to end.</summary>
    [Fact]
    public async Task ScreenshotImageV1_RoundTripsThroughTheRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, ScreenshotImageRuntimeProxy>();
        var proxy = (ScreenshotImageRuntimeProxy)(object)application;
        var installationId = $"screenshot-image-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));
        const string screenshotPath = @"C:\retained\capture.webp";

        var result = await client.GetScreenshotImageAsync(
            new ScreenshotImageRequest(screenshotPath),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("screenshot.image.loaded", result.Code);
        Assert.Equal("capture", result.Value?.ArtifactIdentity);
        Assert.Equal(new byte[] { 1, 4, 9, 16 }, result.Value?.Content);
        Assert.Equal(screenshotPath, proxy.Request?.ScreenshotPath);
    }

    [Fact]
    public async Task ReportSnapshotV6_RoundTripsActivityScoresAndHourlyInstallationProfiles()
    {
        var installation = InstallationProfileCatalog.CreateDefault(
            Guid.NewGuid().ToString("N"),
            "Workstation",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var snapshot = new ReportSnapshot(
            6,
            new ReportRange(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 2), "UTC", 2),
            new ReportTotals(60, 0, 60, 40, 8, 1),
            [
                new ReportCalendarCell(new DateOnly(2026, 2, 1), 60, 0, 60, 40, 8, 1, true, 62, [installation]),
                new ReportCalendarCell(new DateOnly(2026, 2, 2), 0, 0, 0, 0, 0, 0, false, null, [])
            ],
            [
                new ReportHourCell(0, 12, 60, 0, 60, 1, true, 40, 8, 1, 62, [installation]),
                new ReportHourCell(0, 13, 0, 0, 0, 0, false, 0, 0, 0, null, [])
            ],
            [],
            [],
            new ReportDataQuality(true, null, null, 1, 60, 172_800, 60d / 172_800d),
            AiUsageSummary.Empty);
        var response = new RuntimeResponseEnvelope(
            RuntimeProtocol.ProtocolVersion,
            Guid.NewGuid(),
            true,
            "report.loaded",
            "ReportLoaded",
            snapshot,
            []);
        await using var stream = new MemoryStream();

        await RuntimeProtocol.WriteAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        var actualEnvelope = await RuntimeProtocol.ReadAsync<RuntimeResponseEnvelope>(stream, CancellationToken.None);
        var payload = Assert.IsType<JsonElement>(actualEnvelope.Payload);
        var actual = payload.Deserialize<ReportSnapshot>(RuntimeProtocol.SerializerOptions);

        Assert.NotNull(actual);
        Assert.Equal(6, actual.ContractVersion);
        Assert.Equal(62, actual.Calendar[0].ActivityScore);
        Assert.Null(actual.Calendar[1].ActivityScore);
        Assert.Equal(installation, Assert.Single(actual.HourOfWeek[0].Installations));
        Assert.Equal(62, actual.HourOfWeek[0].ActivityScore);
        Assert.Empty(actual.HourOfWeek[1].Installations);
    }

    [Fact]
    public void ReportHourCell_RejectsPayloadWithoutInstallationProvenance()
    {
        const string payload = """
            {"dayOfWeek":0,"hour":12,"activeSeconds":60,"idleSeconds":0,"trackedSeconds":60,
             "observationDays":1,"hasData":true,"keyPresses":40,"mouseClicks":8,"sampleCount":1,"activityScore":62}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReportHourCell>(payload, RuntimeProtocol.SerializerOptions));
    }

    [Fact]
    public async Task RuntimeHost_KeepsServingHealthAndCancelsReportWhenClientDisconnects()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, ConcurrentRuntimeProxy>();
        var proxy = (ConcurrentRuntimeProxy)(object)application;
        var installationId = $"runtime-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));
        using var reportCancellation = new CancellationTokenSource();
        var reportTask = client.GetReportAsync(
            new ReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "UTC"),
            reportCancellation.Token);
        await proxy.ReportStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var health = await client.GetRuntimeHealthAsync(CancellationToken.None);

        Assert.True(health.Succeeded);
        reportCancellation.Cancel();
        var report = await reportTask;
        Assert.False(report.Succeeded);
        Assert.Equal("operation.cancelled", report.Code);
        await proxy.ReportCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RuntimeHost_DoesNotCreateASecondApplicationWhenOwnershipIsAlreadyHeld()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, ConcurrentRuntimeProxy>();
        var installationId = $"runtime-ownership-test-{Guid.NewGuid():N}";
        await using var owner = new RuntimeHost(application, installationId);
        Assert.True(owner.TryStart());
        var factoryCalls = 0;
        await using var contender = new RuntimeHost(
            () =>
            {
                factoryCalls++;
                return application;
            },
            installationId);

        Assert.False(contender.TryStart());
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task RuntimeHost_HoldsOwnershipUntilFactoryOwnedApplicationIsDisposed()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, BlockingDisposeRuntimeProxy>();
        var proxy = (BlockingDisposeRuntimeProxy)(object)application;
        var installationId = $"runtime-dispose-test-{Guid.NewGuid():N}";
        await using var owner = new RuntimeHost(() => application, installationId);
        Assert.True(owner.TryStart());

        var disposeTask = owner.DisposeAsync().AsTask();
        try
        {
            await proxy.DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await using var contender = new RuntimeHost(
                () => throw new InvalidOperationException("A contender must not create an application."),
                installationId);

            Assert.False(contender.TryStart());

            proxy.AllowDispose.TrySetResult();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));

            var successorApplication = DispatchProxy.Create<IWorkTrailApplication, ConcurrentRuntimeProxy>();
            await using var successor = new RuntimeHost(successorApplication, installationId);
            Assert.True(successor.TryStart());
        }
        finally
        {
            proxy.AllowDispose.TrySetResult();
            await disposeTask;
        }
    }

    [Fact]
    public async Task RuntimeClient_DoesNotCaptureABlockedCallerSynchronizationContext()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, DelayedHealthRuntimeProxy>();
        var installationId = $"runtime-context-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));
        using var completed = new ManualResetEventSlim();
        OperationResult<RuntimeHealth>? result = null;
        Exception? failure = null;
        var caller = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                result = client.GetRuntimeHealthAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "WorkTrail runtime blocked-context test"
        };

        caller.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "RuntimeClient captured the caller synchronization context and deadlocked.");
        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task StartupMutation_WaitsPastTheDefaultRuntimeTimeout()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, DelayedStartupRuntimeProxy>();
        var installationId = $"startup-timeout-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(1));

        var health = await client.GetRuntimeHealthAsync(CancellationToken.None);
        var startup = await client.SetStartupEnabledAsync(true, CancellationToken.None);

        Assert.True(health.Succeeded);
        Assert.True(startup.Succeeded);
        Assert.True(startup.Value);
    }

    [Fact]
    public async Task WorldClockQuery_UsesItsWeatherAwareTimeoutAndKeepsLocalClocksOnWeatherFailure()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, WeatherUnavailableWorldClockRuntimeProxy>();
        var installationId = $"world-clock-timeout-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.Zero);

        var result = await client.GetWorldClocksAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(15), RuntimeClient.WorldClockQueryTimeout);
        Assert.True(result.Succeeded);
        var clock = Assert.Single(result.Value!.Clocks);
        Assert.Equal("london", clock.CityId);
        Assert.True(clock.IsDaylightSavingTime);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), clock.DaylightSavingEndsAt);
        Assert.Null(clock.Weather);
        Assert.Equal("unavailable", result.Value.WeatherStatus.State);
        Assert.Equal("request-failed", result.Value.WeatherStatus.ReasonCode);
    }

    [Fact]
    public async Task WorldClockWeatherKey_RoundTripsThroughDedicatedRuntimeEndpointWithoutEchoingTheSecret()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, WeatherKeyRuntimeProxy>();
        var proxy = Assert.IsAssignableFrom<WeatherKeyRuntimeProxy>(application);
        var installationId = $"world-clock-weather-key-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.Zero);
        const string secret = "0123456789abcdef0123456789abcdef";

        var result = await client.SetWorldClockWeatherKeyAsync(secret, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(15), RuntimeClient.WorldClockWeatherKeyTimeout);
        Assert.True(result.Succeeded);
        Assert.Equal("WORKTRAIL_OPENWEATHER_API_KEY", result.Value);
        Assert.Equal(secret, proxy.Secret);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorldClockMove_RoundTripsCityAndDirectionThroughTheRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, WorldClockMoveRuntimeProxy>();
        var proxy = Assert.IsAssignableFrom<WorldClockMoveRuntimeProxy>(application);
        var installationId = $"world-clock-move-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));

        var result = await client.MoveWorldClockAsync(
            "tokyo",
            WorldClockMoveDirection.Down,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("tokyo", proxy.CityId);
        Assert.Equal(WorldClockMoveDirection.Down, proxy.Direction);
        Assert.Equal(["london", "tokyo"], result.Value?.CityIds);
    }

    [Fact]
    public async Task AiModelCatalog_RoundTripsThroughTheRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, CatalogRuntimeProxy>();
        var installationId = $"catalog-runtime-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));

        var result = await client.GetAiModelCatalogAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.SchemaVersion);
        var model = Assert.Single(result.Value.Models);
        Assert.Equal("gpt-test", model.Key);
        Assert.Equal(["auto", "low"], model.SupportedThinkingEfforts);
    }

    [Fact]
    public async Task AiPricingOverview_RoundTripsThroughTheRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, PricingRuntimeProxy>();
        var installationId = $"pricing-runtime-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));

        var result = await client.GetAiPricingOverviewAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.DisplayedModelCount);
        Assert.Equal(0.0003m, result.Value.EstimatedCostTodayUsd);
        var row = Assert.Single(result.Value.Models);
        Assert.Equal("gpt-test", row.Model);
        Assert.Equal(1.25m, row.InputUsdPerMillionTokens);
        Assert.Equal(10m, row.OutputUsdPerMillionTokens);
    }

    [Fact]
    public async Task AiScreenshotReprocessing_RoundTripsPreviewAndJobCommandsThroughRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, AiScreenshotReprocessRuntimeProxy>();
        var installationId = $"ai-reprocess-runtime-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));
        var date = new DateOnly(2026, 8, 14);

        var preview = await client.PreviewAiScreenshotReprocessingAsync(
            new AiScreenshotReprocessRequest(date),
            CancellationToken.None);
        var started = await client.StartAiScreenshotReprocessingAsync(
            AiScreenshotReprocessRuntimeProxy.PlanId,
            CancellationToken.None);
        var paused = await client.PauseAiScreenshotReprocessingAsync(
            AiScreenshotReprocessRuntimeProxy.JobId,
            CancellationToken.None);

        Assert.True(preview.Succeeded);
        Assert.Equal(date, preview.Value?.Date);
        Assert.Equal(5, preview.Value?.MissingDescriptionScreenshotCount);
        Assert.True(started.Succeeded);
        Assert.Equal(AiScreenshotReprocessJobStatuses.Running, started.Value?.Status);
        Assert.True(paused.Succeeded);
        Assert.Equal(AiScreenshotReprocessJobStatuses.PauseRequested, paused.Value?.Status);
    }

    [Fact]
    public async Task SearchAvailability_RoundTripsThroughTheRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, SearchAvailabilityRuntimeProxy>();
        var installationId = $"search-availability-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));

        var result = await client.GetSearchAvailabilityAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new SearchAvailability(14, 3, true), result.Value);
    }

    [Fact]
    public async Task ApplicationNotifications_RoundTripThroughTheSharedRuntimeFacade()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, NotificationRuntimeProxy>();
        var installationId = $"notification-runtime-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));

        var result = await client.DrainApplicationNotificationsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        var notification = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ApplicationNotification>>(result.Value));
        Assert.Equal(ApplicationNotificationSeverity.Error, notification.Severity);
        Assert.Equal("Notification.AiAnalysisFailed.Message", notification.MessageKey);
    }

    /// <summary>Verifies that screenshot-analysis deletion round-trips through protocol version 4.</summary>
    [Fact]
    public async Task ScreenshotAnalysisDeletionV1_RoundTripsThroughProtocolVersion5RuntimeFacade()
    {
        Assert.Equal(5, RuntimeProtocol.ProtocolVersion);
        var application = DispatchProxy.Create<IWorkTrailApplication, ScreenshotAnalysisDeletionRuntimeProxy>();
        var proxy = (ScreenshotAnalysisDeletionRuntimeProxy)(object)application;
        var installationId = $"screenshot-analysis-deletion-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installationId);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installationId, TimeSpan.FromSeconds(3));
        const string screenshotPath = @"C:\captures\capture.webp";

        var result = await client.DeleteScreenshotAnalysisAsync(screenshotPath, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("screenshot.analysis.deleted", result.Code);
        Assert.Equal(screenshotPath, result.Value);
        Assert.Equal(screenshotPath, proxy.ScreenshotPath);
    }

    public class ConcurrentRuntimeProxy : DispatchProxy
    {
        public TaskCompletionSource<bool> ReportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReportCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetReportAsync) => WaitForReportCancellationAsync((CancellationToken)args![1]!),
                nameof(IWorkTrailApplication.GetRuntimeHealthAsync) => Task.FromResult(OperationResult<RuntimeHealth>.Success(
                    "runtime.healthy",
                    "RuntimeHealthy",
                    new RuntimeHealth(
                        "test",
                        RuntimeProtocol.ProtocolVersion,
                        "test",
                        true,
                        ["report.query.v1"],
                        new TrackingRuntimeHealth(false, null, null, "tracking.persistence.healthy")))),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private async Task<OperationResult<ReportSnapshot>> WaitForReportCancellationAsync(CancellationToken cancellationToken)
        {
            ReportStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The report cancellation test completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
                ReportCancelled.TrySetResult(true);
                throw;
            }
        }
    }

    public class DelayedHealthRuntimeProxy : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetRuntimeHealthAsync) => GetHealthAsync(),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private static async Task<OperationResult<RuntimeHealth>> GetHealthAsync()
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            return OperationResult<RuntimeHealth>.Success(
                "runtime.healthy",
                "RuntimeHealthy",
                new RuntimeHealth(
                    "test",
                    RuntimeProtocol.ProtocolVersion,
                    "test",
                    true,
                    ["runtime.health"],
                    new TrackingRuntimeHealth(false, null, null, "tracking.persistence.healthy")));
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        /// <inheritdoc />
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Deliberately never pump queued callbacks: production IPC must not depend on a UI dispatcher continuation.
        }
    }

    public class CatalogRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetAiModelCatalogAsync) => Task.FromResult(
                    OperationResult<AiModelCatalogSnapshot>.Success(
                        "ai.models.loaded",
                        "AiModelsLoaded",
                        new AiModelCatalogSnapshot(
                            1,
                            [new AiModelDescriptor("gpt-test", ["gpt-test-alias"], "Test", "Test model", "#123456", ["auto", "low"], true, "general", false)]))),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }

    public class PricingRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetAiPricingOverviewAsync) => Task.FromResult(
                    OperationResult<AiPricingOverview>.Success(
                        "ai.pricing.loaded",
                        "AiPricingLoaded",
                        new AiPricingOverview(
                            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
                            1,
                            1,
                            0.0003m,
                            1,
                            null,
                            0,
                            100,
                            20,
                            120,
                            new DateOnly(2026, 8, 1),
                            new DateOnly(2026, 8, 9),
                            0.0012m,
                            null,
                            [new AiPricingCostRow("gpt-test", 1.25m, 10m)]))),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }

    public class NotificationRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.DrainApplicationNotificationsAsync) => Task.FromResult(
                    OperationResult<IReadOnlyList<ApplicationNotification>>.Success(
                        "notifications.drained",
                        "ApplicationNotificationsDrained",
                        [new ApplicationNotification(
                            Guid.Parse("638ba5bb-5074-44f5-85da-da172add83d1"),
                            new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
                            ApplicationNotificationSeverity.Error,
                            "Notification.AiAnalysisFailed.Title",
                            "Notification.AiAnalysisFailed.Message",
                            "ai.provider.failed")])),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }

    public class AiScreenshotReprocessRuntimeProxy : DispatchProxy
    {
        public static Guid PlanId { get; } = Guid.Parse("06df0bfa-7180-4431-a1c2-b104f986238f");
        public static Guid JobId { get; } = Guid.Parse("50617e02-0a8d-4eb6-9e29-3458b72d7f1c");

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.PreviewAiScreenshotReprocessingAsync) => Task.FromResult(
                    OperationResult<AiScreenshotReprocessPlan>.Success(
                        "ai.screenshot_reprocess.previewed",
                        "AiScreenshotReprocessPreviewed",
                        new AiScreenshotReprocessPlan(
                            PlanId,
                            DateTimeOffset.UtcNow.AddMinutes(2),
                            ((AiScreenshotReprocessRequest)args![0]!).Date,
                            5,
                            3,
                            5,
                            3,
                            0,
                            0,
                            0,
                            4,
                            20,
                            16,
                            3,
                            5,
                            0.01m,
                            "test-provider",
                            "test-model",
                            true,
                            null,
                            null))),
                nameof(IWorkTrailApplication.StartAiScreenshotReprocessingAsync) => Task.FromResult(
                    OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                        "ai.screenshot_reprocess.started",
                        "AiScreenshotReprocessStarted",
                        Job(AiScreenshotReprocessJobStatuses.Running))),
                nameof(IWorkTrailApplication.PauseAiScreenshotReprocessingAsync) => Task.FromResult(
                    OperationResult<AiScreenshotReprocessJobSnapshot>.Success(
                        "ai.screenshot_reprocess.pause_requested",
                        "AiScreenshotReprocessPauseRequested",
                        Job(AiScreenshotReprocessJobStatuses.PauseRequested))),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private static AiScreenshotReprocessJobSnapshot Job(string status) => new(
            JobId,
            new DateOnly(2026, 8, 14),
            status,
            3,
            5,
            0,
            0,
            3,
            5,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public class DelayedStartupRuntimeProxy : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetRuntimeHealthAsync) => Task.FromResult(OperationResult<RuntimeHealth>.Success(
                    "runtime.healthy",
                    "RuntimeHealthy",
                    new RuntimeHealth(
                        "test",
                        RuntimeProtocol.ProtocolVersion,
                        "test",
                        true,
                        ["startup.enable"],
                        new TrackingRuntimeHealth(false, null, null, "tracking.persistence.healthy")))),
                nameof(IWorkTrailApplication.SetStartupEnabledAsync) => CompleteStartupAsync((CancellationToken)args![1]!),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private static async Task<OperationResult<bool>> CompleteStartupAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            return OperationResult<bool>.Success("startup.enabled", "StartupEnabled", true);
        }
    }

    public class ScreenshotAnalysisDeletionRuntimeProxy : DispatchProxy
    {
        public string? ScreenshotPath { get; private set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.DeleteScreenshotAnalysisAsync) => DeleteAnalysis((string)args![0]!),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private Task<OperationResult<string>> DeleteAnalysis(string screenshotPath)
        {
            ScreenshotPath = screenshotPath;
            return Task.FromResult(OperationResult<string>.Success(
                "screenshot.analysis.deleted",
                "ScreenshotAnalysisDeleted",
                screenshotPath));
        }
    }

    public class ScreenshotImageRuntimeProxy : DispatchProxy
    {
        public ScreenshotImageRequest? Request { get; private set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetScreenshotImageAsync) => GetScreenshotImage(
                    (ScreenshotImageRequest)args![0]!),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private Task<OperationResult<ScreenshotImageContent>> GetScreenshotImage(ScreenshotImageRequest request)
        {
            Request = request;
            return Task.FromResult(OperationResult<ScreenshotImageContent>.Success(
                "screenshot.image.loaded",
                "ScreenshotImageLoaded",
                new ScreenshotImageContent("capture", [1, 4, 9, 16])));
        }
    }

    public class WeatherUnavailableWorldClockRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetWorldClocksAsync) => Task.FromResult(
                    OperationResult<WorldClockSnapshot>.Success(
                        "world_clocks.loaded",
                        "WorldClocksLoaded",
                        CreateSnapshot())),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private static WorldClockSnapshot CreateSnapshot()
        {
            var instant = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
            return new WorldClockSnapshot(
                instant,
                [
                    new WorldClockItem(
                        "london",
                        "London",
                        "GB",
                        "GMT Standard Time",
                        instant,
                        true,
                        new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero),
                        true,
                        instant.AddHours(-6),
                        instant.AddHours(6),
                        180d,
                        "Assets/WorldClocks/Skylines/london-summer.png",
                        "summer",
                        new WorldClockAtmosphere("day", [], []),
                        Weather: null)
                ],
                WorldClockSelection.MaximumClocks,
                new WorldClockWeatherStatus(
                    "openweather",
                    "unavailable",
                    "request-failed",
                    1,
                    0,
                    IsProviderConfigured: true),
                new WorldClockMapProjection(
                    new WorldClockMapCoordinate(8d, 0d),
                    new WorldClockMapCoordinate(-12d, 145d),
                    180d,
                    [new WorldClockMapCity("london", "London", 51.5074d, -0.1278d)]));
        }
    }

    public class WeatherKeyRuntimeProxy : DispatchProxy
    {
        public string? Secret { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.SetWorldClockWeatherKeyAsync) => StoreSecret((string)args![0]!),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private Task<OperationResult<string>> StoreSecret(string secret)
        {
            Secret = secret;
            return Task.FromResult(OperationResult<string>.Success(
                "world_clocks.weather.key.stored",
                "WorldClockWeatherKeyStored",
                "WORKTRAIL_OPENWEATHER_API_KEY"));
        }
    }

    public class WorldClockMoveRuntimeProxy : DispatchProxy
    {
        public string? CityId { get; private set; }

        public WorldClockMoveDirection Direction { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.MoveWorldClockAsync) => MoveClock(
                    (string)args![0]!,
                    (WorldClockMoveDirection)args[1]!),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private Task<OperationResult<WorldClockSelectionState>> MoveClock(
            string cityId,
            WorldClockMoveDirection direction)
        {
            CityId = cityId;
            Direction = direction;
            return Task.FromResult(OperationResult<WorldClockSelectionState>.Success(
                "world_clocks.moved",
                "WorldClocksMoved",
                new WorldClockSelectionState(["london", "tokyo"], WorldClockSelection.MaximumClocks)));
        }
    }

    public class BlockingDisposeRuntimeProxy : DispatchProxy
    {
        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IAsyncDisposable.DisposeAsync) => new ValueTask(DisposeCoreAsync()),
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }

        private async Task DisposeCoreAsync()
        {
            DisposeStarted.TrySetResult();
            await AllowDispose.Task;
        }
    }

    public class SearchAvailabilityRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IWorkTrailApplication.GetSearchAvailabilityAsync) => Task.FromResult(
                    OperationResult<SearchAvailability>.Success(
                        "search.availability.loaded",
                        "SearchAvailabilityLoaded",
                        new SearchAvailability(14, 3, true))),
                nameof(IAsyncDisposable.DisposeAsync) => ValueTask.CompletedTask,
                "add_RuntimeStateChanged" or "remove_RuntimeStateChanged" => null,
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
        }
    }
}
