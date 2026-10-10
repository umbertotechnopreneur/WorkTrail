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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Presentation;
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class PresentationBoundaryTests
{
    [Fact]
    public void PresentationAssembly_DoesNotReferenceWinUiOrSpectre()
    {
        var references = typeof(WorkTrail.Presentation.MainViewModel).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain(references, name => name?.Contains("WinUI", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(references, name => name?.Contains("Spectre", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task MainViewModel_StartsTrackingFromThePersistedLaunchPreferenceWithoutToggling()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, StartupRecordingApplicationProxy>();
        var recorder = (StartupRecordingApplicationProxy)(object)application;
        var viewModel = new MainViewModel(application);

        var result = await viewModel.InitializeAsync(LaunchOptions.Parse(Array.Empty<string>()), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value?.Dashboard.IsTracking);
        Assert.Same(recorder.LastSession, result.Value?.LastSession);
        Assert.Equal(1, recorder.StartCalls);
        Assert.Equal(0, recorder.ToggleCalls);
        Assert.Equal("winui.launch", recorder.LastStartRequest?.Source);
    }

    public class StartupRecordingApplicationProxy : DispatchProxy
    {
        public int StartCalls { get; private set; }

        public int ToggleCalls { get; private set; }

        public StartTrackingRequest? LastStartRequest { get; private set; }

        public LastSessionState LastSession { get; } = new(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "Editor",
            "Document",
            "test-installation",
            null,
            @"C:\captures\latest.webp",
            DateTimeOffset.UtcNow);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IWorkTrailApplication.GetSettingsAsync):
                    return Task.FromResult(OperationResult<AppSettings>.Success(
                        "settings.loaded",
                        "SettingsLoaded",
                        new AppSettings(StartTrackingOnLaunch: true)));
                case nameof(IWorkTrailApplication.StartTrackingAsync):
                    StartCalls++;
                    LastStartRequest = Assert.IsType<StartTrackingRequest>(args![0]);
                    return Task.FromResult(OperationResult<DashboardState>.Success(
                        "tracking.started",
                        "TrackingStarted",
                        Dashboard(isTracking: true)));
                case nameof(IWorkTrailApplication.GetLastSessionAsync):
                    return Task.FromResult(OperationResult<LastSessionState?>.Success(
                        "session.last.loaded",
                        "LastSessionLoaded",
                        LastSession));
                case nameof(IWorkTrailApplication.ToggleTrackingAsync):
                    ToggleCalls++;
                    return Task.FromResult(OperationResult<DashboardState>.Success(
                        "tracking.toggled",
                        "TrackingToggled",
                        Dashboard(isTracking: false)));
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }

        private static DashboardState Dashboard(bool isTracking) => new(
            isTracking ? "RUNNING" : "PAUSED",
            "STATE_READY",
            0,
            0,
            0,
            0,
            isTracking,
            null,
            DateTimeOffset.Now,
            DateTimeOffset.UtcNow);
    }
}
