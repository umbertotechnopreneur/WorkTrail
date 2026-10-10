// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Controls;
using Windows.Foundation;

namespace WorkTrail;

/// <summary>Owns the lifecycle of timed toast components without coupling notifications to dialog presentation.</summary>
internal sealed class ToastNotificationService
{
    private static readonly TimeSpan TimeoutInterval = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<TimedInfoBar, ToastCountdown> _countdowns = [];
    private TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
    private long _nextGeneration;
    private bool _isEnabled = true;

    /// <summary>Applies the persisted notification preference and dismisses existing toasts when paused.</summary>
    /// <param name="enabled">Whether automatic in-app toasts may be displayed.</param>
    internal void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        if (!enabled) HideAll();
    }

    /// <summary>Gets or sets the timeout used when a toast does not provide an override.</summary>
    internal TimeSpan DefaultTimeout
    {
        get => _defaultTimeout;
        set => _defaultTimeout = ValidateTimeout(value, nameof(value));
    }

    /// <summary>Shows an informational toast in its existing UI component.</summary>
    internal void ShowInfo(TimedInfoBar host, string title, string message, TimeSpan? timeout = null) =>
        Show(host, title, message, InfoBarSeverity.Informational, timeout);

    /// <summary>Shows a successful-operation toast in its existing UI component.</summary>
    internal void ShowSuccess(TimedInfoBar host, string title, string message, TimeSpan? timeout = null) =>
        Show(host, title, message, InfoBarSeverity.Success, timeout);

    /// <summary>Shows a warning toast in its existing UI component.</summary>
    internal void ShowWarning(TimedInfoBar host, string title, string message, TimeSpan? timeout = null) =>
        Show(host, title, message, InfoBarSeverity.Warning, timeout);

    /// <summary>Shows an error toast in its existing UI component.</summary>
    internal void ShowError(TimedInfoBar host, string title, string message, TimeSpan? timeout = null) =>
        Show(host, title, message, InfoBarSeverity.Error, timeout);

    /// <summary>Hides the toast component and stops its timeout lifecycle.</summary>
    internal void Hide(TimedInfoBar host)
    {
        ValidateHostThread(host);
        StopCountdown(host);
        host.Dismiss();
    }

    /// <summary>Stops all active timers and dismisses their hosts before application shutdown.</summary>
    internal void HideAll()
    {
        var hosts = _countdowns.Keys.ToArray();
        foreach (var host in hosts)
        {
            ValidateHostThread(host);
        }

        foreach (var host in hosts)
        {
            // Shutdown must not start a fade or enqueue another callback against a closing XAML root.
            StopCountdown(host);
            host.DismissImmediately();
        }
    }

    // host identifies the existing toast surface.
    // title contains the notification heading.
    // message contains optional supporting text.
    // severity selects the notification color and icon.
    // timeout overrides the default dismissal delay when supplied.
    private void Show(TimedInfoBar host, string title, string message, InfoBarSeverity severity, TimeSpan? timeout)
    {
        ValidateHostThread(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(message);
        var duration = ValidateTimeout(timeout ?? DefaultTimeout, nameof(timeout));
        if (!_isEnabled) return;
        StopCountdown(host);
        host.Dismissed += ToastHost_Dismissed;
        host.Present(title, message, severity);

        var timer = host.DispatcherQueue.CreateTimer();
        timer.Interval = TimeoutInterval;
        timer.IsRepeating = true;
        var generation = ++_nextGeneration;
        TypedEventHandler<DispatcherQueueTimer, object> tick = (_, _) => UpdateCountdown(host, generation);
        _countdowns[host] = new ToastCountdown(timer, tick, Stopwatch.GetTimestamp(), duration, generation);
        timer.Tick += tick;
        timer.Start();
    }

    // host identifies the toast whose dismissal timeout is being checked.
    // generation prevents an earlier timer from dismissing a replacement notification.
    private void UpdateCountdown(TimedInfoBar host, long generation)
    {
        if (!_countdowns.TryGetValue(host, out var countdown) || countdown.Generation != generation)
        {
            return;
        }

        // Monotonic time keeps the timeout stable even when Windows clock time changes.
        if (Stopwatch.GetElapsedTime(countdown.StartedTimestamp) < countdown.Duration)
        {
            return;
        }

        Hide(host);
    }

    private void ToastHost_Dismissed(object? sender, EventArgs e)
    {
        if (sender is TimedInfoBar host)
        {
            StopCountdown(host);
        }
    }

    private void StopCountdown(TimedInfoBar host)
    {
        host.Dismissed -= ToastHost_Dismissed;
        if (_countdowns.Remove(host, out var countdown))
        {
            countdown.Timer.Stop();
            countdown.Timer.Tick -= countdown.Tick;
        }
    }

    private static void ValidateHostThread(TimedInfoBar host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.DispatcherQueue.HasThreadAccess)
        {
            // Presentation has no cross-dispatcher fallback: show and hide must use the host UI thread.
            throw new InvalidOperationException("Toasts must be controlled from their host UI thread.");
        }
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, timeout, "Toast timeout must be greater than zero.");
        }

        return timeout;
    }

    private sealed record ToastCountdown(
        DispatcherQueueTimer Timer,
        TypedEventHandler<DispatcherQueueTimer, object> Tick,
        long StartedTimestamp,
        TimeSpan Duration,
        long Generation);
}
