// SPDX-License-Identifier: MIT
namespace WorkTrail.Services;

/// <summary>Bounds the settling delay before an automatic foreground capture.</summary>
internal sealed record ScreenshotStabilityOptions(
    bool Enabled = true,
    int SettleMilliseconds = 1000,
    int MaximumWaitMilliseconds = 3000,
    int PollMilliseconds = 100,
    int RetrySeconds = 3,
    int MaxRetries = 2)
{
    internal void Validate()
    {
        if (SettleMilliseconds is < 100 or > 5000
            || MaximumWaitMilliseconds < SettleMilliseconds
            || MaximumWaitMilliseconds > 10000
            || PollMilliseconds is < 25 or > 1000
            || PollMilliseconds > SettleMilliseconds
            || RetrySeconds is < 1 or > 60
            || MaxRetries is < 0 or > 5)
            throw new InvalidDataException("Screenshot stability settings require a bounded settling delay, polling interval and retry interval.");
    }
}

/// <summary>Contains only the foreground values whose changes restart the settling delay.</summary>
internal sealed record ScreenshotForegroundIdentity(
    IntPtr WindowHandle,
    string ProcessName,
    string WindowTitle,
    int Left,
    int Top,
    int Right,
    int Bottom);

/// <summary>Evaluates one capture attempt using observations from a monotonic millisecond clock.</summary>
internal sealed class ScreenshotStabilityPolicy
{
    private readonly ScreenshotStabilityOptions _options;
    private ScreenshotForegroundIdentity? _identity;
    private long _startedAt;
    private long _unchangedSince;
    private long _lastObservedAt;

    internal ScreenshotStabilityPolicy(ScreenshotStabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    internal bool Observe(ScreenshotForegroundIdentity identity, long monotonicMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateTimestamp(monotonicMilliseconds);
        if (!_options.Enabled) return true;

        if (_identity is null)
        {
            _startedAt = monotonicMilliseconds;
            _unchangedSince = monotonicMilliseconds;
        }
        else if (_identity != identity)
        {
            // Returning to an earlier window is still a new transition, not previously earned stability.
            _unchangedSince = monotonicMilliseconds;
        }

        _identity = identity;
        _lastObservedAt = monotonicMilliseconds;
        return monotonicMilliseconds - _unchangedSince >= _options.SettleMilliseconds
            && monotonicMilliseconds - _startedAt <= _options.MaximumWaitMilliseconds;
    }

    internal bool IsExpired(long monotonicMilliseconds)
    {
        ValidateTimestamp(monotonicMilliseconds);
        return _options.Enabled && _identity is not null
            && monotonicMilliseconds - _startedAt >= _options.MaximumWaitMilliseconds;
    }

    private void ValidateTimestamp(long monotonicMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(monotonicMilliseconds);
        if (_identity is not null && monotonicMilliseconds < _lastObservedAt)
            throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds), "Capture observations require a monotonic clock.");
    }
}
