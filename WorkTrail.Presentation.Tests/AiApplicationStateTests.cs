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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class AiApplicationStateTests
{
    [Fact]
    public async Task SharedState_RefreshesBindingsAfterKeyAndEnabledChanges()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, RecordingAiApplicationProxy>();
        var recorder = (RecordingAiApplicationProxy)(object)application;
        var state = new AiApplicationState(application);
        var changedProperties = new HashSet<string>(StringComparer.Ordinal);
        state.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName ?? string.Empty);

        await state.LoadAsync(CancellationToken.None);

        Assert.True(state.IsKeyMissing);
        Assert.False(state.IsStatusUnavailable);
        Assert.False(state.HasKey);
        Assert.False(state.CanEnable);
        Assert.False(state.CanToggle);
        Assert.NotNull(state.CostGate);
        Assert.Equal(0, state.CostGate!.DailyAnalysisCount);

        var stored = await state.SetSecretAsync("OPENAI_API_KEY", "not-retained-by-state", CancellationToken.None);

        Assert.True(stored.Succeeded);
        Assert.True(state.HasKey);
        Assert.True(state.CanEnable);
        Assert.True(state.CanToggle);
        Assert.False(state.IsKeyMissing);
        Assert.False(state.HasInvalidKey);
        Assert.Equal(2, recorder.StatusReads);

        var enabled = await state.SetEnabledAsync(true, CancellationToken.None);

        Assert.True(enabled.Succeeded);
        Assert.True(state.Enabled);
        Assert.Contains(nameof(AiApplicationState.CanEnable), changedProperties);
        Assert.Contains(nameof(AiApplicationState.Enabled), changedProperties);
    }

    [Fact]
    public async Task EnabledMutation_DisablesBothConsumersUntilItCompletes()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, RecordingAiApplicationProxy>();
        var recorder = (RecordingAiApplicationProxy)(object)application;
        var state = new AiApplicationState(application);
        await state.LoadAsync(CancellationToken.None);
        await state.SetSecretAsync("OPENAI_API_KEY", "not-retained-by-state", CancellationToken.None);
        recorder.HoldEnable = true;

        var pending = state.SetEnabledAsync(true, CancellationToken.None);
        var overlapping = await state.SetEnabledAsync(false, CancellationToken.None);

        Assert.True(state.IsBusy);
        Assert.False(state.CanToggle);
        Assert.False(overlapping.Succeeded);
        Assert.Equal("ai.state.busy", overlapping.Code);

        recorder.CompleteEnable(true);
        var completed = await pending;

        Assert.True(completed.Succeeded);
        Assert.True(state.Enabled);
        Assert.False(state.IsBusy);
        Assert.True(state.CanToggle);
    }

    [Fact]
    public async Task SecretRefreshFailure_IsReportedAsUnavailableInsteadOfMissing()
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, RecordingAiApplicationProxy>();
        var recorder = (RecordingAiApplicationProxy)(object)application;
        var state = new AiApplicationState(application);
        await state.LoadAsync(CancellationToken.None);
        recorder.FailNextStatusRead = true;

        var result = await state.SetSecretAsync("OPENAI_API_KEY", "not-retained-by-state", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ai.key.stored_status_unavailable", result.Code);
        Assert.True(state.IsStatusUnavailable);
        Assert.False(state.IsKeyMissing);
        Assert.False(state.CanToggle);
        Assert.Null(state.CostGate);
    }

    public class RecordingAiApplicationProxy : DispatchProxy
    {
        private AiStatus _status = Status(enabled: false, hasKey: false, canEnable: false);
        private TaskCompletionSource<OperationResult<AiStatus>>? _enableCompletion;

        public int StatusReads { get; private set; }

        public bool FailNextStatusRead { get; set; }

        public bool HoldEnable { get; set; }

        public void CompleteEnable(bool enabled)
        {
            _status = _status with { Enabled = enabled };
            _enableCompletion!.SetResult(OperationResult<AiStatus>.Success("ai.enabled", "AiEnabled", _status));
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IWorkTrailApplication.GetAiStatusAsync))
            {
                StatusReads++;
                if (FailNextStatusRead)
                {
                    FailNextStatusRead = false;
                    return Task.FromResult(OperationResult<AiStatus>.Failure("ai.status.unavailable", "AiStatusUnavailable"));
                }

                return Task.FromResult(OperationResult<AiStatus>.Success("ai.status.loaded", "AiStatusLoaded", _status));
            }

            if (targetMethod?.Name == nameof(IWorkTrailApplication.SetAiEnabledAsync))
            {
                if (HoldEnable)
                {
                    _enableCompletion = new TaskCompletionSource<OperationResult<AiStatus>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    return _enableCompletion.Task;
                }

                _status = _status with { Enabled = Assert.IsType<bool>(args![0]) };
                return Task.FromResult(OperationResult<AiStatus>.Success("ai.enabled", "AiEnabled", _status));
            }

            if (targetMethod?.Name == nameof(IWorkTrailApplication.SetAiKeyAsync))
            {
                _status = _status with { HasKey = true, CanEnable = true };
                return Task.FromResult(OperationResult<string>.Success("ai.key.stored", "AiKeyStored", "OPENAI_API_KEY"));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }

        private static AiStatus Status(bool enabled, bool hasKey, bool canEnable) => new(
            enabled,
            "openai",
            "gpt-5.6",
            "https://api.openai.com/v1/responses",
            "OPENAI_API_KEY",
            hasKey,
            canEnable,
            new AnalysisCostGate(true, null, 0m, 0, 0m));
    }
}
