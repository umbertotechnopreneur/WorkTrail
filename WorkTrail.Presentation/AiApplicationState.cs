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


using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Provides one observable, non-secret AI state shared by all application surfaces.</summary>
public sealed class AiApplicationState : ViewModelBase
{
    private readonly IWorkTrailApplication _application;
    private bool _enabled;
    private bool _hasKey;
    private bool _canEnable;
    private bool _canToggle;
    private bool _isKeyMissing;
    private bool _hasInvalidKey;
    private bool _isStatusUnavailable = true;
    private bool _isBusy;
    private AnalysisCostGate? _costGate;

    /// <summary>Initializes the shared AI state with the application facade.</summary>
    public AiApplicationState(IWorkTrailApplication application) => _application = application;

    /// <summary>Gets whether AI integration is currently enabled.</summary>
    public bool Enabled { get => _enabled; private set => Set(ref _enabled, value); }

    /// <summary>Gets whether the configured environment variable contains any key value.</summary>
    public bool HasKey { get => _hasKey; private set => Set(ref _hasKey, value); }

    /// <summary>Gets whether the configured key is plausible enough to allow AI activation.</summary>
    public bool CanEnable { get => _canEnable; private set => Set(ref _canEnable, value); }

    /// <summary>Gets whether the toggle may be used to enable AI or to turn off an already-enabled invalid state.</summary>
    public bool CanToggle { get => _canToggle; private set => Set(ref _canToggle, value); }

    /// <summary>Gets whether no API key is available.</summary>
    public bool IsKeyMissing { get => _isKeyMissing; private set => Set(ref _isKeyMissing, value); }

    /// <summary>Gets whether a key exists but does not have a plausible provider-specific shape.</summary>
    public bool HasInvalidKey { get => _hasInvalidKey; private set => Set(ref _hasInvalidKey, value); }

    /// <summary>Gets whether the application could not determine the current API-key state.</summary>
    public bool IsStatusUnavailable { get => _isStatusUnavailable; private set => Set(ref _isStatusUnavailable, value); }

    /// <summary>Gets whether a shared AI-state mutation is currently running.</summary>
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    /// <summary>Gets the latest privacy-safe daily AI usage gate returned by the application facade.</summary>
    public AnalysisCostGate? CostGate { get => _costGate; private set => Set(ref _costGate, value); }

    /// <summary>Reloads redacted AI state from the application facade.</summary>
    public async Task<OperationResult<AiStatus>> LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<AiStatus>.Failure("ai.state.busy", "AiStateBusy");
        }

        var result = await _application.GetAiStatusAsync(cancellationToken);
        if (result.Succeeded && result.Value is not null)
        {
            Apply(result.Value);
        }
        else
        {
            MarkUnavailable();
        }

        return result;
    }

    /// <summary>Requests an enabled-state change and publishes the returned persisted state.</summary>
    public async Task<OperationResult<AiStatus>> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<AiStatus>.Failure("ai.state.busy", "AiStateBusy");
        }

        SetBusy(true);
        try
        {
            var result = await _application.SetAiEnabledAsync(enabled, cancellationToken);
            if (result.Succeeded && result.Value is not null)
            {
                Apply(result.Value);
            }

            return result;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Forwards a secret without retaining it, then refreshes the shared redacted state.</summary>
    public async Task<OperationResult<string>> SetSecretAsync(string variable, string secret, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return OperationResult<string>.Failure("ai.state.busy", "AiStateBusy");
        }

        SetBusy(true);
        try
        {
            var result = await _application.SetAiKeyAsync(variable, secret, cancellationToken);
            if (!result.Succeeded)
            {
                return result;
            }

            var refresh = await _application.GetAiStatusAsync(cancellationToken);
            if (!refresh.Succeeded || refresh.Value is null)
            {
                MarkUnavailable();
                return OperationResult<string>.Failure(
                    "ai.key.stored_status_unavailable",
                    "AiKeyStoredStatusUnavailable",
                    refresh.Issues.ToArray());
            }

            Apply(refresh.Value);
            return result;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Apply(AiStatus status)
    {
        Enabled = status.Enabled;
        HasKey = status.HasKey;
        CanEnable = status.CanEnable;
        IsStatusUnavailable = false;
        IsKeyMissing = !status.HasKey;
        HasInvalidKey = status.HasKey && !status.CanEnable;
        CostGate = status.CostGate;
        UpdateCanToggle();
    }

    private void MarkUnavailable()
    {
        IsStatusUnavailable = true;
        HasKey = false;
        CanEnable = false;
        IsKeyMissing = false;
        HasInvalidKey = false;
        CostGate = null;
        UpdateCanToggle();
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        UpdateCanToggle();
    }

    private void UpdateCanToggle() => CanToggle = !IsBusy && !IsStatusUnavailable && (Enabled || CanEnable);
}
