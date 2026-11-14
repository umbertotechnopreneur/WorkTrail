// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Owns the planner's reference input and last complete projection independently of its controls.</summary>
public sealed class AvailabilityPlannerSession
{
    private string? _cityId;
    private DateTime? _date;
    private TimeSpan _time = TimeSpan.MinValue;
    private bool _isLive = true;

    /// <summary>Gets the last successfully loaded timetable.</summary>
    public AvailabilityPlannerSnapshot? Snapshot { get; private set; }

    /// <summary>Gets or sets the committed time step for subsequent requests.</summary>
    public int StepMinutes { get; set; } = 30;

    /// <summary>Gets the current complete request, or null while an edit is incomplete.</summary>
    public AvailabilityPlannerRequest? Request => _isLive
        ? new(_cityId, StepMinutes: StepMinutes)
        : _cityId is not null && _date is { } date && _time >= TimeSpan.Zero
            ? new(_cityId, date.Date + _time, StepMinutes)
            : null;

    /// <summary>Captures the user's reference input for conversion, even while incomplete.</summary>
    public void EditReference(string? cityId, DateTime? date, TimeSpan time)
    {
        _cityId = cityId;
        _date = date;
        _time = time;
        _isLive = false;
    }

    /// <summary>Uses the current instant in the selected city.</summary>
    public void UseLive() => _isLive = true;

    /// <summary>Reconciles a removed reference city before the next projection request.</summary>
    public void ReconcileCities(IReadOnlyList<WorldClockItem> cities)
    {
        if (_cityId is null || cities.Any(city => city.CityId == _cityId))
        {
            return;
        }

        _cityId = cities.FirstOrDefault()?.CityId;
        if (Snapshot?.Cities.FirstOrDefault(city => city.Clock.CityId == _cityId) is { } replacement)
        {
            _date = replacement.Clock.LocalTime.Date;
            _time = replacement.Clock.LocalTime.TimeOfDay;
        }
        else
        {
            UseLive();
        }
    }

    /// <summary>Commits the result and its resolved reference input.</summary>
    public void Apply(AvailabilityPlannerSnapshot snapshot)
    {
        Snapshot = snapshot;
        _cityId = snapshot.ReferenceCityId;
        _date = snapshot.ReferenceLocalTime.Date;
        _time = snapshot.ReferenceLocalTime.TimeOfDay;
    }
}
