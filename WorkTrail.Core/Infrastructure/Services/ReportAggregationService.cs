// SPDX-License-Identifier: MIT

using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Builds privacy-safe reports from the SQLite activity history.</summary>
public sealed class ReportAggregationService
{
    /// <summary>Gets the maximum inclusive local-date range accepted by a report query.</summary>
    public const int MaximumRangeDays = 366;

    private const int ContractVersion = 6;
    private const int DefaultApplicationLimit = 12;
    private readonly LocalStore _store;

    /// <summary>Initializes an aggregate report service over the shared local store.</summary>
    public ReportAggregationService(LocalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Builds an aggregate report, returning validation issues for invalid user ranges.</summary>
    public OperationResult<ReportSnapshot> Build(ReportQuery query, CancellationToken cancellationToken) =>
        Build(query, DefaultApplicationLimit, cancellationToken);

    /// <summary>Builds an aggregate report with a caller-selected application limit.</summary>
    internal OperationResult<ReportSnapshot> Build(
        ReportQuery query,
        int applicationLimit,
        CancellationToken cancellationToken,
        string? installationId = null)
    {
        if (query is null)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.query.required",
                "ReportQueryRequired",
                new ValidationIssue("query", "required", "ReportQueryRequired"));
        }

        if (query.From > query.ToInclusive)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.range.invalid",
                "ReportRangeInvalid",
                new ValidationIssue("toInclusive", "before_from", "ReportRangeInvalid"));
        }

        var dayCount = query.ToInclusive.DayNumber - query.From.DayNumber + 1;
        if (dayCount > MaximumRangeDays)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.range.too_large",
                "ReportRangeTooLarge",
                new ValidationIssue("toInclusive", "maximum_366_days", "ReportRangeTooLarge"));
        }

        if (query.ToInclusive == DateOnly.MaxValue)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.range.invalid",
                "ReportRangeInvalid",
                new ValidationIssue("toInclusive", "out_of_range", "ReportRangeInvalid"));
        }

        if (applicationLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(applicationLimit));
        }

        var timeZoneResult = ResolveTimeZone(query.TimeZoneId);
        if (timeZoneResult.TimeZone is null)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.time_zone.invalid",
                "ReportTimeZoneInvalid",
                new ValidationIssue("timeZoneId", "invalid", "ReportTimeZoneInvalid"));
        }

        DateTimeOffset fromUtc;
        DateTimeOffset toUtc;
        try
        {
            fromUtc = ConvertLocalBoundaryToUtc(query.From, timeZoneResult.TimeZone);
            toUtc = ConvertLocalBoundaryToUtc(query.ToInclusive.AddDays(1), timeZoneResult.TimeZone);
        }
        catch (ArgumentException)
        {
            return OperationResult<ReportSnapshot>.Failure(
                "report.range.invalid",
                "ReportRangeInvalid",
                new ValidationIssue("range", "unrepresentable", "ReportRangeInvalid"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var installationProfiles = _store.GetInstallationProfiles()
            .ToDictionary(profile => profile.InstallationId, StringComparer.Ordinal);
        var aggregation = new AggregationState(
            query.From,
            dayCount,
            timeZoneResult.TimeZone,
            fromUtc,
            toUtc,
            installationProfiles);
        var aiUsage = new AiUsageAccumulator(new AiTokenCostEstimator(_store.ListAiModelPricing(AiPricingProviders.OpenAi)));

        // Both forward-only readers share one SQLite read transaction; no raw activity sample crosses this boundary.
        _store.VisitReportData(
            fromUtc,
            toUtc,
            sample => { if (installationId is null || sample.InstallationId == installationId) aggregation.AddSample(sample); },
            usage => { if (installationId is null) aiUsage.Add(usage); },
            cancellationToken);

        var snapshot = aggregation.BuildSnapshot(
            query.ToInclusive,
            timeZoneResult.TimeZone.Id,
            applicationLimit,
            aiUsage.Build());
        return OperationResult<ReportSnapshot>.Success("report.loaded", "ReportLoaded", snapshot);
    }

    /// <summary>
    /// Aggregates only AI usage for a trusted local-date range, avoiding the activity-history scan
    /// required by a full report.
    /// </summary>
    internal AiUsageSummary BuildAiUsage(
        DateOnly from,
        DateOnly toInclusive,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (from > toInclusive)
        {
            throw new ArgumentException("The AI usage range end must not precede its start.", nameof(toInclusive));
        }

        if (toInclusive == DateOnly.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(toInclusive));
        }

        var dayCount = toInclusive.DayNumber - from.DayNumber + 1;
        if (dayCount > MaximumRangeDays)
        {
            throw new ArgumentOutOfRangeException(nameof(toInclusive));
        }

        var fromUtc = ConvertLocalBoundaryToUtc(from, timeZone);
        var toUtc = ConvertLocalBoundaryToUtc(toInclusive.AddDays(1), timeZone);
        var usage = new AiUsageAccumulator(new AiTokenCostEstimator(_store.ListAiModelPricing(AiPricingProviders.OpenAi)));
        _store.VisitAiUsage(fromUtc, toUtc, usage.Add, cancellationToken);
        return usage.Build();
    }

    private static (TimeZoneInfo? TimeZone, string? Error) ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return (TimeZoneInfo.Local, null);
        }

        try
        {
            return (TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()), null);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return (null, exception.GetType().Name);
        }
    }

    internal static DateTimeOffset ConvertLocalBoundaryToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The larger ambiguous offset selects the first occurrence of a repeated local boundary.
            var offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    private sealed class AggregationState
    {
        private readonly DateOnly _from;
        private readonly int _dayCount;
        private readonly TimeZoneInfo _timeZone;
        private readonly long _fromUtcTicks;
        private readonly long _toUtcTicks;
        private readonly IReadOnlyDictionary<string, InstallationProfile> _installationProfiles;
        private readonly Dictionary<DateOnly, DayAccumulator> _days;
        private readonly Dictionary<(int DayOfWeek, int Hour), HourAccumulator> _hours;
        private readonly Dictionary<string, OrderedIntervalUnionAccumulator> _applicationIntervals = new(StringComparer.OrdinalIgnoreCase);
        private readonly OrderedIntervalUnionAccumulator _coverage = new();
        private DateTimeOffset? _firstSampleAt;
        private DateTimeOffset? _lastSampleAt;
        private int _sampleCount;

        internal AggregationState(
            DateOnly from,
            int dayCount,
            TimeZoneInfo timeZone,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            IReadOnlyDictionary<string, InstallationProfile> installationProfiles)
        {
            _from = from;
            _dayCount = dayCount;
            _timeZone = timeZone;
            _fromUtcTicks = fromUtc.UtcDateTime.Ticks;
            _toUtcTicks = toUtc.UtcDateTime.Ticks;
            _installationProfiles = installationProfiles ?? throw new ArgumentNullException(nameof(installationProfiles));
            _days = Enumerable.Range(0, dayCount)
                .ToDictionary(offset => from.AddDays(offset), _ => new DayAccumulator());
            _hours = Enumerable.Range(0, 7)
                .SelectMany(day => Enumerable.Range(0, 24).Select(hour => (day, hour)))
                .ToDictionary(key => key, _ => new HourAccumulator());
        }

        internal void AddSample(ReportSourceSample sample)
        {
            var originalDurationTicks = checked((long)sample.DurationSeconds * TimeSpan.TicksPerSecond);
            var originalEndTicks = sample.Timestamp.UtcDateTime.Ticks;
            var originalStartTicks = checked(originalEndTicks - originalDurationTicks);
            var clippedStartTicks = Math.Max(originalStartTicks, _fromUtcTicks);
            var clippedEndTicks = Math.Min(originalEndTicks, _toUtcTicks);
            if (clippedStartTicks >= clippedEndTicks)
            {
                return;
            }

            if (!_installationProfiles.TryGetValue(sample.InstallationId, out var installation))
            {
                // Incomplete provenance invalidates the report; never attribute a sample to another system.
                throw new InvalidDataException("An activity sample references an unknown installation profile.");
            }

            _sampleCount++;
            if (_firstSampleAt is null || sample.Timestamp < _firstSampleAt.Value)
            {
                _firstSampleAt = sample.Timestamp;
            }

            if (_lastSampleAt is null || sample.Timestamp > _lastSampleAt.Value)
            {
                _lastSampleAt = sample.Timestamp;
            }

            AddCoverage(clippedStartTicks, clippedEndTicks);
            var segments = SplitIntoLocalHourSegments(clippedStartTicks, clippedEndTicks, _timeZone);
            var includedDurationTicks = clippedEndTicks - clippedStartTicks;
            var includedKeyPresses = ScaleCount(sample.KeyPresses, includedDurationTicks, originalDurationTicks);
            var includedMouseClicks = ScaleCount(sample.MouseClicks, includedDurationTicks, originalDurationTicks);
            var keyPresses = AllocateCount(includedKeyPresses, segments, includedDurationTicks);
            var mouseClicks = AllocateCount(includedMouseClicks, segments, includedDurationTicks);
            var sampleDates = new Dictionary<DateOnly, (long StartTicks, long EndTicks)>();
            var sampleHours = new HashSet<(int DayOfWeek, int Hour)>();
            var isActive = string.Equals(sample.State, "active", StringComparison.OrdinalIgnoreCase);

            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                if (!_days.TryGetValue(segment.Bucket.Date, out var day))
                {
                    throw new InvalidOperationException("An activity segment fell outside the normalized report range.");
                }

                day.KeyPresses += keyPresses[index];
                day.MouseClicks += mouseClicks[index];

                var hour = _hours[(segment.Bucket.DayOfWeek, segment.Bucket.Hour)];
                hour.TrackedIntervals.Add(segment.StartTicks, segment.EndTicks);
                hour.Installations.Add(installation);
                hour.ObservationDates.Add(segment.Bucket.Date);
                hour.KeyPresses += keyPresses[index];
                hour.MouseClicks += mouseClicks[index];
                if (sampleHours.Add((segment.Bucket.DayOfWeek, segment.Bucket.Hour)))
                {
                    hour.SampleCount++;
                }
                if (isActive)
                {
                    hour.ActiveIntervals.Add(segment.StartTicks, segment.EndTicks);
                }

                var dayStartTicks = sampleDates.TryGetValue(segment.Bucket.Date, out var dayInterval)
                    ? dayInterval.StartTicks
                    : segment.StartTicks;
                sampleDates[segment.Bucket.Date] = (dayStartTicks, segment.EndTicks);
            }

            foreach (var (date, interval) in sampleDates)
            {
                var day = _days[date];
                // Add each sample's daily coverage once: its hourly fragments can overtake the
                // start of an overlapping sample from another installation at an hour boundary.
                day.TrackedIntervals.Add(interval.StartTicks, interval.EndTicks);
                if (isActive)
                {
                    day.ActiveIntervals.Add(interval.StartTicks, interval.EndTicks);
                }

                day.SampleCount++;
                day.Installations.Add(installation);
            }

            if (isActive)
            {
                var application = string.IsNullOrWhiteSpace(sample.Application) ? "Unknown" : sample.Application.Trim();
                if (!_applicationIntervals.TryGetValue(application, out var intervals))
                {
                    intervals = new OrderedIntervalUnionAccumulator();
                    _applicationIntervals.Add(application, intervals);
                }

                intervals.Add(clippedStartTicks, clippedEndTicks);
            }
        }

        internal ReportSnapshot BuildSnapshot(
            DateOnly toInclusive,
            string timeZoneId,
            int applicationLimit,
            AiUsageSummary aiUsage)
        {
            var calendar = _days
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.ToCalendarCell(pair.Key))
                .ToArray();
            var hours = _hours
                .OrderBy(pair => pair.Key.DayOfWeek)
                .ThenBy(pair => pair.Key.Hour)
                .Select(pair => pair.Value.ToHourCell(pair.Key.DayOfWeek, pair.Key.Hour))
                .ToArray();
            var trend = calendar
                .Select(day => new ReportTrendBucket(
                    day.Date,
                    day.Date,
                    day.ActiveSeconds,
                    day.IdleSeconds,
                    day.TrackedSeconds,
                    day.KeyPresses,
                    day.MouseClicks,
                    day.HasData))
                .ToArray();
            var applications = BuildApplications(applicationLimit);
            var activeSeconds = calendar.Sum(day => day.ActiveSeconds);
            var idleSeconds = calendar.Sum(day => day.IdleSeconds);
            var trackedSeconds = calendar.Sum(day => day.TrackedSeconds);
            var requestedSeconds = (_toUtcTicks - _fromUtcTicks) / TimeSpan.TicksPerSecond;
            var coveredSeconds = _coverage.Complete() / TimeSpan.TicksPerSecond;
            var coverageRatio = requestedSeconds == 0
                ? 0d
                : Math.Clamp(coveredSeconds / (double)requestedSeconds, 0d, 1d);

            return new ReportSnapshot(
                ContractVersion,
                new ReportRange(_from, toInclusive, timeZoneId, _dayCount),
                new ReportTotals(
                    activeSeconds,
                    idleSeconds,
                    trackedSeconds,
                    calendar.Sum(day => day.KeyPresses),
                    calendar.Sum(day => day.MouseClicks),
                    calendar.Count(day => day.ActiveSeconds > 0)),
                calendar,
                hours,
                trend,
                applications,
                new ReportDataQuality(
                    _sampleCount > 0,
                    _firstSampleAt,
                    _lastSampleAt,
                    _sampleCount,
                    coveredSeconds,
                    requestedSeconds,
                    coverageRatio),
                aiUsage);
        }

        private IReadOnlyList<ReportApplicationSlice> BuildApplications(int applicationLimit)
        {
            // Each application is unioned independently. Parallel different applications can therefore
            // exceed the wall-clock active total; the report has no cross-application attribution model.
            var ordered = _applicationIntervals
                .Select(pair => new ReportApplicationSlice(pair.Key, pair.Value.Complete() / TimeSpan.TicksPerSecond))
                .Where(slice => slice.ActiveSeconds > 0)
                .OrderByDescending(slice => slice.ActiveSeconds)
                .ThenBy(slice => slice.Application, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ordered.Length <= applicationLimit)
            {
                return ordered;
            }

            var result = ordered.Take(applicationLimit).ToList();
            result.Add(new ReportApplicationSlice("Other", ordered.Skip(applicationLimit).Sum(slice => slice.ActiveSeconds)));
            return result;
        }

        private void AddCoverage(long startTicks, long endTicks)
            => _coverage.Add(startTicks, endTicks);
    }

    internal static List<ActivitySegment> SplitIntoLocalHourSegments(
        long startTicks,
        long endTicks,
        TimeZoneInfo timeZone)
    {
        var segments = new List<ActivitySegment>(2);
        var currentTicks = startTicks;
        while (currentTicks < endTicks)
        {
            var bucket = GetBucket(currentTicks, timeZone);
            var probeEndTicks = Math.Min(currentTicks + TimeSpan.TicksPerHour, endTicks);
            long segmentEndTicks;
            if (GetBucket(probeEndTicks - 1, timeZone) == bucket)
            {
                segmentEndTicks = probeEndTicks;
            }
            else
            {
                var low = currentTicks + 1;
                var high = probeEndTicks - 1;
                while (low < high)
                {
                    var middle = low + ((high - low) / 2);
                    if (GetBucket(middle, timeZone) == bucket)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                segmentEndTicks = low;
            }

            segments.Add(new ActivitySegment(currentTicks, segmentEndTicks, bucket));
            currentTicks = segmentEndTicks;
        }

        return segments;
    }

    private static LocalBucket GetBucket(long utcTicks, TimeZoneInfo timeZone)
    {
        var utc = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return new LocalBucket(DateOnly.FromDateTime(local.DateTime), (int)local.DayOfWeek, local.Hour);
    }

    private static long ScaleCount(long count, long includedTicks, long originalTicks) =>
        includedTicks == originalTicks
            ? count
            : decimal.ToInt64(decimal.Round((decimal)count * includedTicks / originalTicks, 0, MidpointRounding.AwayFromZero));

    private static long[] AllocateCount(
        long total,
        IReadOnlyList<ActivitySegment> segments,
        long totalTicks)
    {
        var result = new long[segments.Count];
        long allocated = 0;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            var segmentTicks = segments[index].EndTicks - segments[index].StartTicks;
            result[index] = decimal.ToInt64(decimal.Floor((decimal)total * segmentTicks / totalTicks));
            allocated += result[index];
        }

        if (segments.Count > 0)
        {
            result[^1] = total - allocated;
        }

        return result;
    }

    internal readonly record struct LocalBucket(DateOnly Date, int DayOfWeek, int Hour);

    internal readonly record struct ActivitySegment(long StartTicks, long EndTicks, LocalBucket Bucket);

    private sealed class DayAccumulator
    {
        internal InstallationAccumulator Installations { get; } = new();
        internal OrderedIntervalUnionAccumulator ActiveIntervals { get; } = new();
        internal OrderedIntervalUnionAccumulator TrackedIntervals { get; } = new();
        internal long KeyPresses { get; set; }
        internal long MouseClicks { get; set; }
        internal int SampleCount { get; set; }

        internal ReportCalendarCell ToCalendarCell(DateOnly date)
        {
            var hasData = SampleCount > 0;
            var trackedTicks = TrackedIntervals.Complete();
            var activeTicks = ActiveIntervals.Complete();
            if (activeTicks > trackedTicks)
            {
                throw new InvalidOperationException("Active report coverage cannot exceed tracked coverage.");
            }

            var trackedSeconds = trackedTicks / TimeSpan.TicksPerSecond;
            var activeSeconds = Math.Min(trackedSeconds, activeTicks / TimeSpan.TicksPerSecond);
            var idleSeconds = trackedSeconds - activeSeconds;
            int? activityScore = hasData
                ? ActivityScoreService.CalculateHistoricalActivityScore(
                    KeyPresses,
                    MouseClicks,
                    activeTicks / (double)TimeSpan.TicksPerSecond,
                    trackedTicks / (double)TimeSpan.TicksPerSecond)
                : null;
            return new ReportCalendarCell(
                date,
                activeSeconds,
                idleSeconds,
                trackedSeconds,
                KeyPresses,
                MouseClicks,
                SampleCount,
                hasData,
                activityScore,
                Installations.Build());
        }
    }

    private sealed class HourAccumulator
    {
        internal InstallationAccumulator Installations { get; } = new();
        internal long KeyPresses { get; set; }
        internal long MouseClicks { get; set; }
        internal int SampleCount { get; set; }
        internal OrderedIntervalUnionAccumulator ActiveIntervals { get; } = new();
        internal OrderedIntervalUnionAccumulator TrackedIntervals { get; } = new();
        internal HashSet<DateOnly> ObservationDates { get; } = [];

        internal ReportHourCell ToHourCell(int dayOfWeek, int hour)
        {
            var trackedTicks = TrackedIntervals.Complete();
            var activeTicks = ActiveIntervals.Complete();
            if (activeTicks > trackedTicks)
            {
                throw new InvalidOperationException("Active hourly coverage cannot exceed tracked coverage.");
            }

            var trackedSeconds = MeanSeconds(trackedTicks);
            var activeSeconds = Math.Min(trackedSeconds, MeanSeconds(activeTicks));
            return new ReportHourCell(
                dayOfWeek,
                hour,
                activeSeconds,
                trackedSeconds - activeSeconds,
                trackedSeconds,
                ObservationDates.Count,
                ObservationDates.Count > 0,
                KeyPresses,
                MouseClicks,
                SampleCount,
                ObservationDates.Count > 0
                    ? ActivityScoreService.CalculateHistoricalActivityScore(
                        KeyPresses, MouseClicks,
                        activeTicks / (double)TimeSpan.TicksPerSecond,
                        trackedTicks / (double)TimeSpan.TicksPerSecond)
                    : null,
                Installations.Build());
        }

        private long MeanSeconds(long ticks) => ObservationDates.Count == 0
            ? 0
            : checked((long)Math.Round(
                ticks / (double)TimeSpan.TicksPerSecond / ObservationDates.Count,
                MidpointRounding.AwayFromZero));
    }

    private sealed class InstallationAccumulator
    {
        private readonly Dictionary<string, InstallationProfile> _installations = new(StringComparer.Ordinal);

        internal void Add(InstallationProfile installation)
        {
            if (_installations.TryGetValue(installation.InstallationId, out var existing)
                && existing != installation)
            {
                // A snapshot cannot mix revisions of one profile; report the invalid state to the caller.
                throw new InvalidDataException("An installation profile changed inside one report snapshot.");
            }

            _installations[installation.InstallationId] = installation;
        }

        internal IReadOnlyList<InstallationProfile> Build() => _installations.Values
            .OrderBy(profile => profile.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.MachineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.InstallationId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Merges a start-ordered interval stream without retaining raw report samples.</summary>
    private sealed class OrderedIntervalUnionAccumulator
    {
        private long? _openStartTicks;
        private long _openEndTicks;
        private long _lastStartTicks;
        private long _completedTicks;
        private bool _hasLastStart;
        private bool _completed;

        internal void Add(long startTicks, long endTicks)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Completed report interval coverage cannot accept more samples.");
            }

            if (startTicks >= endTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(endTicks), "Report intervals must have positive duration.");
            }

            if (_hasLastStart && startTicks < _lastStartTicks)
            {
                throw new InvalidOperationException("Report intervals must be supplied in start-time order.");
            }

            _hasLastStart = true;
            _lastStartTicks = startTicks;
            if (_openStartTicks is null)
            {
                _openStartTicks = startTicks;
                _openEndTicks = endTicks;
                return;
            }

            if (startTicks <= _openEndTicks)
            {
                _openEndTicks = Math.Max(_openEndTicks, endTicks);
                return;
            }

            _completedTicks = checked(_completedTicks + _openEndTicks - _openStartTicks.Value);
            _openStartTicks = startTicks;
            _openEndTicks = endTicks;
        }

        internal long Complete()
        {
            if (_completed)
            {
                return _completedTicks;
            }

            if (_openStartTicks is not null)
            {
                _completedTicks = checked(_completedTicks + _openEndTicks - _openStartTicks.Value);
                _openStartTicks = null;
                _openEndTicks = 0;
            }

            _completed = true;
            return _completedTicks;
        }
    }

    private sealed class AiUsageAccumulator(AiTokenCostEstimator estimator)
    {
        private readonly Dictionary<string, AiUsageSliceAccumulator> _providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AiUsageSliceAccumulator> _origins = new(StringComparer.OrdinalIgnoreCase);
        private int _requestCount;
        private int _successfulRequestCount;
        private int _failedRequestCount;
        private long _inputTokens;
        private long _outputTokens;
        private long _totalTokens;
        private long _cachedInputTokens;
        private long _reasoningTokens;
        private long _thinkingTokens;
        private decimal _actualCostUsd;
        private int _actualCostRequestCount;
        private decimal _estimatedCostUsd;
        private int _estimatedCostRequestCount;
        private DateTimeOffset? _estimatedCostPricingUpdatedAt;

        internal void Add(AiRequestUsageRecord request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var inputTokens = request.Usage.InputTokens ?? 0;
            var outputTokens = request.Usage.OutputTokens ?? 0;
            var totalTokens = request.Usage.TotalTokens ?? checked(inputTokens + outputTokens);
            var actualCostUsd = request.Usage.ReportedCostUsd;
            var estimate = estimator.Estimate(request);

            checked
            {
                _requestCount++;
                if (request.Success)
                {
                    _successfulRequestCount++;
                }
                else
                {
                    _failedRequestCount++;
                }

                _inputTokens += inputTokens;
                _outputTokens += outputTokens;
                _totalTokens += totalTokens;
                // Providers use different names for the same cache-read input counter; never double-count it.
                _cachedInputTokens += request.Usage.CachedInputTokens
                    ?? request.Usage.CacheReadInputTokens
                    ?? 0;
                _reasoningTokens += request.Usage.ReasoningTokens ?? 0;
                _thinkingTokens += request.Usage.ThinkingTokens ?? 0;
                if (actualCostUsd.HasValue)
                {
                    _actualCostUsd += actualCostUsd.Value;
                    _actualCostRequestCount++;
                }

                if (estimate is not null)
                {
                    _estimatedCostUsd += estimate.CostUsd;
                    _estimatedCostRequestCount++;
                    if (_estimatedCostPricingUpdatedAt is null
                        || estimate.PricingUpdatedAt > _estimatedCostPricingUpdatedAt.Value)
                    {
                        _estimatedCostPricingUpdatedAt = estimate.PricingUpdatedAt;
                    }
                }
            }

            AddSlice(_providers, NormalizeLabel(request.Provider), inputTokens, outputTokens, totalTokens, actualCostUsd, estimate?.CostUsd);
            AddSlice(_origins, NormalizeLabel(request.Origin), inputTokens, outputTokens, totalTokens, actualCostUsd, estimate?.CostUsd);
        }

        internal AiUsageSummary Build() => _requestCount == 0
            ? AiUsageSummary.Empty
            : new AiUsageSummary(
                _requestCount,
                _successfulRequestCount,
                _failedRequestCount,
                _inputTokens,
                _outputTokens,
                _totalTokens,
                _cachedInputTokens,
                _reasoningTokens,
                _thinkingTokens,
                _actualCostRequestCount == 0 ? null : _actualCostUsd,
                _actualCostRequestCount,
                _estimatedCostRequestCount == 0 ? null : _estimatedCostUsd,
                _estimatedCostRequestCount,
                _estimatedCostPricingUpdatedAt,
                BuildSlices(_providers),
                BuildSlices(_origins));

        private static void AddSlice(
            IDictionary<string, AiUsageSliceAccumulator> slices,
            string label,
            long inputTokens,
            long outputTokens,
            long totalTokens,
            decimal? actualCostUsd,
            decimal? estimatedCostUsd)
        {
            if (!slices.TryGetValue(label, out var slice))
            {
                slice = new AiUsageSliceAccumulator(label);
                slices.Add(label, slice);
            }

            slice.Add(inputTokens, outputTokens, totalTokens, actualCostUsd, estimatedCostUsd);
        }

        private static IReadOnlyList<AiUsageSlice> BuildSlices(
            IReadOnlyDictionary<string, AiUsageSliceAccumulator> slices) => slices.Values
            .OrderByDescending(slice => slice.RequestCount)
            .ThenBy(slice => slice.Label, StringComparer.OrdinalIgnoreCase)
            .Select(slice => slice.Build())
            .ToArray();

        private static string NormalizeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var normalized = value.Trim();
            // Keep SQLite metadata from making the versioned renderer payload invalid.
            return normalized.Length <= 256 ? normalized : normalized[..256];
        }
    }

    private sealed class AiTokenCostEstimator
    {
        private readonly Dictionary<string, AiModelPricing> _prices;

        internal AiTokenCostEstimator(IEnumerable<AiModelPricing> prices)
        {
            _prices = prices
                .Where(price =>
                    string.Equals(price.Provider, AiPricingProviders.OpenAi, StringComparison.Ordinal)
                    && string.Equals(price.ServiceTier, AiPricingServiceTiers.Standard, StringComparison.Ordinal)
                    && string.Equals(price.ContextWindow, AiPricingContextWindows.Short, StringComparison.Ordinal))
                .GroupBy(price => price.Model, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        internal AiCostEstimate? Estimate(AiRequestUsageRecord request)
        {
            // Output validity does not undo token consumption, including incomplete batch responses.
            if (!string.Equals(request.Provider, AiPricingProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                || !TryResolvePrice(request, out var price))
            {
                return null;
            }

            var inputTokens = request.Usage.InputTokens ?? 0;
            var outputTokens = request.Usage.OutputTokens ?? 0;
            var cachedInputTokens = request.Usage.CachedInputTokens
                ?? request.Usage.CacheReadInputTokens
                ?? 0;
            var cacheWriteTokens = request.Usage.CacheWriteTokens ?? 0;
            var uncachedInputTokens = Math.Max(0, inputTokens - cachedInputTokens - cacheWriteTokens);
            if (inputTokens == 0 && outputTokens == 0)
            {
                return null;
            }

            if ((cachedInputTokens > 0 && !price.CachedInputUsdPerMillionTokens.HasValue)
                || (cacheWriteTokens > 0 && !price.CacheWriteUsdPerMillionTokens.HasValue))
            {
                return null;
            }

            var costUsd =
                CalculateTokenCost(uncachedInputTokens, price.InputUsdPerMillionTokens)
                + CalculateTokenCost(cachedInputTokens, price.CachedInputUsdPerMillionTokens ?? 0m)
                + CalculateTokenCost(cacheWriteTokens, price.CacheWriteUsdPerMillionTokens ?? 0m)
                + CalculateTokenCost(outputTokens, price.OutputUsdPerMillionTokens);
            if (request.RequestKind == "report_timesheet_batch") costUsd *= 0.5m;
            return new AiCostEstimate(costUsd, price.SourceRetrievedAt);
        }

        private bool TryResolvePrice(AiRequestUsageRecord request, out AiModelPricing price)
        {
            var model = string.IsNullOrWhiteSpace(request.ReturnedModel)
                ? request.RequestedModel
                : request.ReturnedModel;
            model = model.Trim();
            if (_prices.TryGetValue(model, out var exactPrice))
            {
                price = exactPrice;
                return true;
            }

            if (TryStripDateSuffix(model, out var baseModel)
                && _prices.TryGetValue(baseModel, out var basePrice))
            {
                price = basePrice;
                return true;
            }

            price = default!;
            return false;
        }

        private static bool TryStripDateSuffix(string model, out string baseModel)
        {
            baseModel = model;
            if (model.Length <= 11)
            {
                return false;
            }

            var suffix = model[^11..];
            if (suffix[0] != '-'
                || !IsFourDigits(suffix.AsSpan(1, 4))
                || suffix[5] != '-'
                || !IsTwoDigits(suffix.AsSpan(6, 2))
                || suffix[8] != '-'
                || !IsTwoDigits(suffix.AsSpan(9, 2)))
            {
                return false;
            }

            baseModel = model[..^11];
            return baseModel.Length > 0;
        }

        private static bool IsFourDigits(ReadOnlySpan<char> value) =>
            value.Length == 4 && value[0] is >= '0' and <= '9' && value[1] is >= '0' and <= '9'
                && value[2] is >= '0' and <= '9' && value[3] is >= '0' and <= '9';

        private static bool IsTwoDigits(ReadOnlySpan<char> value) =>
            value.Length == 2 && value[0] is >= '0' and <= '9' && value[1] is >= '0' and <= '9';

        private static decimal CalculateTokenCost(long tokens, decimal usdPerMillionTokens) =>
            tokens <= 0 ? 0m : tokens * usdPerMillionTokens / 1_000_000m;
    }

    private sealed record AiCostEstimate(decimal CostUsd, DateTimeOffset PricingUpdatedAt);

    private sealed class AiUsageSliceAccumulator(string label)
    {
        private decimal _actualCostUsd;
        private int _actualCostRequestCount;
        private decimal _estimatedCostUsd;
        private int _estimatedCostRequestCount;

        internal string Label { get; } = label;

        internal int RequestCount { get; private set; }

        internal long InputTokens { get; private set; }

        internal long OutputTokens { get; private set; }

        internal long TotalTokens { get; private set; }

        internal void Add(
            long inputTokens,
            long outputTokens,
            long totalTokens,
            decimal? actualCostUsd,
            decimal? estimatedCostUsd)
        {
            checked
            {
                RequestCount++;
                InputTokens += inputTokens;
                OutputTokens += outputTokens;
                TotalTokens += totalTokens;
                if (actualCostUsd.HasValue)
                {
                    _actualCostUsd += actualCostUsd.Value;
                    _actualCostRequestCount++;
                }

                if (estimatedCostUsd.HasValue)
                {
                    _estimatedCostUsd += estimatedCostUsd.Value;
                    _estimatedCostRequestCount++;
                }
            }
        }

        internal AiUsageSlice Build() => new(
            Label,
            RequestCount,
            InputTokens,
            OutputTokens,
            TotalTokens,
            _actualCostRequestCount == 0 ? null : _actualCostUsd,
            _estimatedCostRequestCount == 0 ? null : _estimatedCostUsd);
    }
}
