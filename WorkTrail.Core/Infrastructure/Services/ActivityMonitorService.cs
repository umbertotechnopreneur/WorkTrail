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
using System.Diagnostics;
using System.Text;
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Providers;

namespace WorkTrail.Services;

/// <summary>
/// Samples foreground activity, keypresses and mouse clicks at a fixed interval.
/// </summary>
public sealed class ActivityMonitorService : IDisposable
{
    private const int SampleSeconds = 5;
    private const int IdleThresholdSeconds = 60;
    private readonly LocalStore _store;
    private readonly InputHookService _inputHooks;
    private readonly SettingsSnapshot _settingsSnapshot;
    private readonly ActivityContextProviderRegistry _providers = new();
    private readonly object _lifecycleLock = new();
    private readonly object _healthLock = new();
    private readonly string _installationId;
    private TrackingRuntimeHealth _runtimeHealth = new(
        IsDegraded: false,
        LastPersistedSampleAt: null,
        LastPersistenceFailureAt: null,
        StatusCode: "tracking.persistence.healthy");
    private Timer? _timer;
    private int _sampleInProgress;
    private int _sampleThreadId;
    private bool _disposed;

    /// <summary>
    /// Creates a monitor service bound to a store and input counter source.
    /// </summary>
    public ActivityMonitorService(
        LocalStore store,
        InputHookService inputHooks,
        SettingsSnapshot? settingsSnapshot = null)
    {
        _store = store;
        _inputHooks = inputHooks;
        _settingsSnapshot = settingsSnapshot ?? new SettingsSnapshot(store.LoadSettings());
        _installationId = _settingsSnapshot.Value.InstallationId;
    }

    public event Action<ActivitySample>? SampleRecorded;
    internal event Action? SampleSuppressed;
    internal event Action<TrackingRuntimeHealth>? RuntimeHealthChanged;

    public ActivitySample? CurrentSample { get; private set; }

    /// <summary>Gets the latest durable-sampling health without querying SQLite.</summary>
    public TrackingRuntimeHealth RuntimeHealth
    {
        get
        {
            lock (_healthLock)
            {
                return _runtimeHealth;
            }
        }
    }

    /// <summary>
    /// Starts periodic sampling. If already running, it keeps existing cadence.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer ??= new Timer(_ => SampleIfIdle(), null, TimeSpan.Zero, TimeSpan.FromSeconds(SampleSeconds));
        }
    }

    /// <summary>
    /// Stops periodic sampling and releases the timer.
    /// </summary>
    public void Stop()
    {
        Timer? timer;
        lock (_lifecycleLock)
        {
            timer = _timer;
            _timer = null;
        }

        DrainTimer(timer);
    }

    private void SampleIfIdle()
    {
        if (Interlocked.Exchange(ref _sampleInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            Volatile.Write(ref _sampleThreadId, Environment.CurrentManagedThreadId);
            Sample();
        }
        finally
        {
            Volatile.Write(ref _sampleThreadId, 0);
            Volatile.Write(ref _sampleInProgress, 0);
        }
    }

    /// <summary>
    /// Captures one activity point and stores it. Failures are isolated to one sample.
    /// </summary>
    private void Sample()
    {
        ActivitySample sample;
        try
        {
            // Capture failures from protected/transient OS state skip only this observation.
            var window = ReadForegroundWindow(out var windowHandle);
            var context = _providers.Resolve(window);
            var counts = _inputHooks.TakeCounts();
            var state = GetIdleSeconds() >= IdleThresholdSeconds ? "idle" : "active";
            var attributes = context.Attributes is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(context.Attributes, StringComparer.Ordinal);
            var spanLabel = _settingsSnapshot.Value.SpanLabel;
            if (!string.IsNullOrWhiteSpace(spanLabel))
            {
                attributes[ActivityAttributeKeys.SpanLabel] = spanLabel;
            }

            sample = new ActivitySample(DateTimeOffset.Now, SampleSeconds, state, window.ProcessName,
                context.Application, context.Context, window.WindowTitle, _installationId, counts.Keys, counts.Clicks,
                attributes.Count == 0 ? null : attributes)
            {
                ApplicationIconPixels = WindowIconService.ReadPixels(windowHandle)
            };
        }
        catch
        {
            return;
        }

        _ = TryPersistSample(sample);
    }

    /// <summary>
    /// Persists one timer sample without allowing a storage failure to escape the timer callback.
    /// The next periodic sample is the bounded retry; the last durable sample remains current meanwhile.
    /// </summary>
    internal bool TryPersistSample(ActivitySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        try
        {
            PersistSample(sample);
            return true;
        }
        catch
        {
            MarkPersistenceDegraded();
            return false;
        }
    }

    /// <summary>Persists a captured sample; storage failures propagate because tracking has no secondary store.</summary>
    internal void PersistSample(ActivitySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var settings = _settingsSnapshot.Value;
        if ((!string.IsNullOrWhiteSpace(settings.PrivacyWindowHints) && string.IsNullOrWhiteSpace(sample.WindowTitle))
            || TrackingDomainService.IsHistoricalContextPrivate(settings, sample.ProcessName,
            new AnalysisContextSnapshot(sample.Application, sample.Context, sample.WindowTitle, sample.State, sample.Attributes)))
        {
            // Excluded observations (including their input counts) never enter history or AI context.
            CurrentSample = null;
            SampleSuppressed?.Invoke();
            return;
        }

        if (!ActivityContextProviderRegistry.IsDetailEnabled(sample.ProcessName, settings))
        {
            sample = sample with { Application = sample.ProcessName, Context = string.Empty, WindowTitle = string.Empty, Attributes = null };
        }

        _store.AppendSample(sample);
        CurrentSample = sample;
        MarkPersistenceHealthy(sample.Timestamp);
        try
        {
            SampleRecorded?.Invoke(sample);
        }
        catch
        {
            // A presentation subscriber cannot invalidate an already durable activity sample.
        }
    }

    private void MarkPersistenceHealthy(DateTimeOffset persistedAt)
    {
        TrackingRuntimeHealth? changed = null;
        lock (_healthLock)
        {
            var wasDegraded = _runtimeHealth.IsDegraded;
            _runtimeHealth = new TrackingRuntimeHealth(
                IsDegraded: false,
                LastPersistedSampleAt: persistedAt,
                LastPersistenceFailureAt: _runtimeHealth.LastPersistenceFailureAt,
                StatusCode: "tracking.persistence.healthy");
            if (wasDegraded)
            {
                changed = _runtimeHealth;
            }
        }

        PublishRuntimeHealthChange(changed);
    }

    private void MarkPersistenceDegraded()
    {
        TrackingRuntimeHealth? changed = null;
        lock (_healthLock)
        {
            var wasDegraded = _runtimeHealth.IsDegraded;
            _runtimeHealth = new TrackingRuntimeHealth(
                IsDegraded: true,
                LastPersistedSampleAt: CurrentSample?.Timestamp,
                LastPersistenceFailureAt: DateTimeOffset.UtcNow,
                StatusCode: "tracking.persistence.failed");
            if (!wasDegraded)
            {
                changed = _runtimeHealth;
            }
        }

        PublishRuntimeHealthChange(changed);
    }

    private void PublishRuntimeHealthChange(TrackingRuntimeHealth? health)
    {
        if (health is null)
        {
            return;
        }

        try
        {
            RuntimeHealthChanged?.Invoke(health);
        }
        catch
        {
            // Health consumers are supplementary; they cannot rethrow a contained persistence failure.
        }
    }

    /// <summary>
    /// Reads foreground window title and process from OS APIs.
    /// </summary>
    /// <returns>Window metadata for enrichment of activity context.</returns>
    private static ForegroundWindowInfo ReadForegroundWindow(out nint handle)
    {
        handle = NativeMethods.GetForegroundWindow();
        var title = new StringBuilder(1024);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new ForegroundWindowInfo(process.ProcessName, title.ToString());
        }
        catch
        {
            // An unavailable process must not masquerade as a safe process under exclusion rules.
            return new ForegroundWindowInfo(string.Empty, title.ToString());
        }
    }

    /// <summary>
    /// Returns the time elapsed since last user input in seconds.
    /// </summary>
    private static long GetIdleSeconds()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return 0;
        }

        return unchecked((uint)Environment.TickCount - info.Time) / 1000;
    }

    public void Dispose()
    {
        Timer? timer;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        DrainTimer(timer);
    }

    private void DrainTimer(Timer? timer)
    {
        if (timer is null)
        {
            return;
        }

        if (Volatile.Read(ref _sampleThreadId) == Environment.CurrentManagedThreadId)
        {
            // A subscriber may synchronously request pause from SampleRecorded. The current sample
            // is already durable, so prevent future callbacks without waiting on this callback itself.
            timer.Dispose();
            return;
        }

        // DisposeAsync completes only after callbacks that are already running have drained.
        timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
