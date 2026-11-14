// SPDX-License-Identifier: MIT

using WorkTrail.Services;

namespace WorkTrail.Application;

/// <summary>Owns the clock reference and projection shared by the desktop surfaces for one app session.</summary>
/// <remarks>Use from one owning synchronization context so projection changes are delivered in order.</remarks>
public sealed class WorldClockSession(IWorkTrailApplication application)
{
    private readonly SemaphoreSlim _cityChanges = new(1);
    private int _projectionVersion;
    private bool _converting;
    private bool _hasRejectedReference;
    private WorldClockConversionRequest? _fixedRequest;

    /// <summary>Gets the last complete projection.</summary>
    public WorldClockSnapshot? Snapshot { get; private set; }

    /// <summary>Gets the selected reference clock in the last complete projection.</summary>
    public WorldClockItem? ReferenceClock { get; private set; }

    /// <summary>Gets the local reference input, including a pending or rejected edit.</summary>
    public DateTime? ReferenceLocalTime { get; private set; }

    /// <summary>Gets whether the committed projection follows the current time.</summary>
    public bool IsLive => _fixedRequest is null;

    /// <summary>Gets whether the live timer may replace the current projection.</summary>
    public bool CanRefreshLive => IsLive && !_converting;

    /// <summary>Publishes a complete projection and indicates whether to redraw the settings editor.</summary>
    public event Action<WorldClockSnapshot, bool>? ProjectionChanged;

    /// <summary>Publishes changes to the shared list or order of cities.</summary>
    public event Action<WorldClockSnapshot>? CitiesChanged;

    /// <summary>Changes the reference city while retaining the projected instant.</summary>
    public void SelectReference(string cityId)
    {
        if (Snapshot is not { } snapshot || cityId == ReferenceClock?.CityId)
        {
            return;
        }

        var reference = snapshot.Clocks.FirstOrDefault(clock => clock.CityId == cityId);
        if (reference is null)
        {
            return;
        }

        InvalidateConversion();
        Apply(snapshot, reference, updateSettings: true);
    }

    /// <summary>Updates the date input while retaining the selected time of day.</summary>
    public void SetReferenceDate(DateTime date) =>
        ReferenceLocalTime = date.Date + (ReferenceLocalTime?.TimeOfDay ?? TimeSpan.Zero);

    /// <summary>Updates the time input while retaining the selected date.</summary>
    public void SetReferenceTime(TimeSpan time)
    {
        if (ReferenceLocalTime is { } localTime)
        {
            ReferenceLocalTime = localTime.Date + time;
        }
    }

    /// <summary>Returns to the live reference and invalidates any in-flight conversion.</summary>
    public void UseLive()
    {
        InvalidateConversion();
        _fixedRequest = null;
        ReferenceLocalTime = ReferenceClock?.LocalTime.DateTime;
    }

    /// <summary>Refreshes the committed projection and keeps rejected input available for correction.</summary>
    public Task<OperationResult<WorldClockSnapshot>?> RefreshAsync(CancellationToken cancellationToken, bool periodic = false) =>
        _converting
            ? Task.FromResult<OperationResult<WorldClockSnapshot>?>(null)
            : LoadAsync(_fixedRequest,
                converting: false, updateSettings: !periodic || Snapshot is null, cancellationToken,
                preserveDraft: _hasRejectedReference);

    /// <summary>Refreshes a changed city list even when a reference edit needs correction.</summary>
    public async Task<OperationResult<WorldClockSnapshot>?> RefreshCitiesAsync(CancellationToken cancellationToken)
    {
        if (_converting)
        {
            // Retry against the saved selection rather than publishing a projection of the old list.
            var conversion = await ConvertAsync(cancellationToken);
            if (conversion is null or { Succeeded: true, Value: not null })
            {
                return conversion;
            }
        }

        return await RefreshAsync(cancellationToken);
    }

    /// <summary>Applies the reference input, ignoring a response superseded by more recent input.</summary>
    public Task<OperationResult<WorldClockSnapshot>?> ConvertAsync(CancellationToken cancellationToken)
    {
        if (ReferenceClock is not { } reference || ReferenceLocalTime is not { } localTime)
        {
            return Task.FromResult<OperationResult<WorldClockSnapshot>?>(null);
        }

        _converting = true;
        var request = new WorldClockConversionRequest(reference.CityId, DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified));
        return LoadAsync(request, converting: true, updateSettings: true, cancellationToken);
    }

    /// <summary>Returns the catalog entries that can still be added to this selection.</summary>
    public async Task<OperationResult<WorldClockCityCatalog>> GetAvailableCitiesAsync(CancellationToken cancellationToken)
    {
        var result = await application.GetWorldClockCityCatalogAsync(cancellationToken);
        if (!result.Succeeded || result.Value is not { } catalog)
        {
            return result;
        }

        var selectedIds = Snapshot?.Clocks.Select(clock => clock.CityId).ToHashSet(StringComparer.Ordinal) ?? [];
        if (selectedIds.Count >= catalog.MaximumClocks)
        {
            return OperationResult<WorldClockCityCatalog>.Failure("world-clocks.limit", "WorldClocksMaximumReached");
        }

        return result with { Value = catalog with { Cities = catalog.Cities.Where(city => !selectedIds.Contains(city.Id)).ToArray() } };
    }

    /// <summary>Removes a city through the shared runtime and refreshes the projection.</summary>
    public Task<OperationResult<WorldClockSnapshot>?> RemoveCityAsync(string cityId, CancellationToken cancellationToken) =>
        ChangeCitiesAsync(() => application.RemoveWorldClockAsync(cityId, cancellationToken), cancellationToken);

    /// <summary>Moves a city through the shared runtime and refreshes the projection.</summary>
    public Task<OperationResult<WorldClockSnapshot>?> MoveCityAsync(
        string cityId, WorldClockMoveDirection direction, CancellationToken cancellationToken) =>
        ChangeCitiesAsync(() => application.MoveWorldClockAsync(cityId, direction, cancellationToken), cancellationToken);

    private async Task<OperationResult<WorldClockSnapshot>?> ChangeCitiesAsync(
        Func<Task<OperationResult<WorldClockSelectionState>>> change, CancellationToken cancellationToken)
    {
        await _cityChanges.WaitAsync(cancellationToken);
        try
        {
            var result = await change();
            if (!result.Succeeded || result.Value is not { } selection)
            {
                return OperationResult<WorldClockSnapshot>.Failure(result.Code, result.MessageKey);
            }

            // Use another city at the same instant if the current reference was removed.
            if (selection.CityIds.Count == 0)
            {
                UseLive();
            }
            else if (ReferenceClock is { } reference && !selection.CityIds.Contains(reference.CityId))
            {
                InvalidateConversion();
                ReferenceClock = Snapshot?.Clocks.FirstOrDefault(clock => clock.CityId == selection.CityIds[0]);
                ReferenceLocalTime = ReferenceClock?.LocalTime.DateTime;
            }

            if (_fixedRequest is { } anchor && !selection.CityIds.Contains(anchor.ReferenceCityId))
            {
                var replacement = ReferenceClock ?? Snapshot?.Clocks.FirstOrDefault(clock => selection.CityIds.Contains(clock.CityId));
                _fixedRequest = replacement is null ? null : new(replacement.CityId, replacement.LocalTime.DateTime);
            }

            return await RefreshCitiesAsync(cancellationToken);
        }
        finally
        {
            _cityChanges.Release();
        }
    }

    private async Task<OperationResult<WorldClockSnapshot>?> LoadAsync(
        WorldClockConversionRequest? request, bool converting, bool updateSettings, CancellationToken cancellationToken,
        bool preserveDraft = false)
    {
        var version = ++_projectionVersion;
        OperationResult<WorldClockSnapshot> result;
        try
        {
            result = request is not null
                ? await application.ConvertWorldClocksAsync(request, cancellationToken)
                : await application.GetWorldClocksAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (converting && version == _projectionVersion)
            {
                _converting = false;
            }
            return null;
        }
        catch (Exception exception)
        {
            ApplicationErrorService.Record(exception, "WorldClockProjection");
            result = OperationResult<WorldClockSnapshot>.Failure("world-clocks.projection.failed", "WorldClock.CatalogUnavailable");
        }

        if (version != _projectionVersion || cancellationToken.IsCancellationRequested)
        {
            if (converting && version == _projectionVersion)
            {
                _converting = false;
            }
            return null;
        }

        _converting = false;
        if (result.Succeeded && result.Value is { } snapshot)
        {
            _fixedRequest = snapshot.Clocks.Count == 0 ? null : request;
            if (!preserveDraft || IsLive)
            {
                _hasRejectedReference = false;
            }
            var reference = snapshot.Clocks.FirstOrDefault(clock => clock.CityId == ReferenceClock?.CityId) ?? snapshot.Clocks.FirstOrDefault();
            Apply(snapshot, reference, updateSettings, preserveDraft && !IsLive);
        }
        else if (converting)
        {
            // A failed live-to-fixed change restores the live input; a fixed edit remains available for correction.
            _hasRejectedReference = !IsLive;
            if (IsLive && Snapshot is { } liveSnapshot)
            {
                Apply(liveSnapshot, ReferenceClock, updateSettings);
            }
        }

        return result;
    }

    private void Apply(WorldClockSnapshot snapshot, WorldClockItem? reference, bool updateSettings, bool preserveDraft = false)
    {
        var citiesChanged = Snapshot is null || !Snapshot.Clocks.Select(clock => clock.CityId)
            .SequenceEqual(snapshot.Clocks.Select(clock => clock.CityId), StringComparer.Ordinal);
        var offsetsChanged = Snapshot is { } previous && !previous.Clocks.Select(clock => clock.LocalTime.Offset)
            .SequenceEqual(snapshot.Clocks.Select(clock => clock.LocalTime.Offset));
        Snapshot = snapshot;
        ReferenceClock = reference;
        if (!preserveDraft)
        {
            ReferenceLocalTime = reference?.LocalTime.DateTime;
        }
        ProjectionChanged?.Invoke(snapshot, updateSettings || citiesChanged || offsetsChanged);
        if (citiesChanged)
        {
            CitiesChanged?.Invoke(snapshot);
        }
    }

    private void InvalidateConversion()
    {
        _projectionVersion++;
        _converting = false;
        _hasRejectedReference = false;
    }
}
