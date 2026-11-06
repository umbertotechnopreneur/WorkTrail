// SPDX-License-Identifier: MIT
using CosineKitty;

namespace WorkTrail.Application;

/// <summary>Projects UTC slots through local working rules without presentation or network dependencies.</summary>
internal static class AvailabilityPlannerService
{
    /// <summary>Creates editable initial rules for a city.</summary>
    internal static AvailabilitySchedule DefaultSchedule(string cityId, bool isHome, string countryCode = "") => new(cityId,
        new TimeOnly(isHome ? 5 : 9, 0), new TimeOnly(isHome ? 23 : 18, 0),
        isHome ? [] : Enum.GetValues<DayOfWeek>().Where(day => RegionalWeekend.IsWeekend(countryCode, day)).ToArray());

    /// <summary>Rejects unsupported ranges before publishing or persisting a schedule.</summary>
    internal static bool IsValid(AvailabilitySchedule schedule) =>
        schedule.Start != schedule.End && schedule.Start.Ticks % TimeSpan.TicksPerMinute == 0 && schedule.End.Ticks % TimeSpan.TicksPerMinute == 0
        && schedule.RestDays is not null && schedule.RestDays.All(Enum.IsDefined)
        && schedule.ReserveBeforeMinutes is >= 0 and <= 360 && schedule.ReserveAfterMinutes is >= 0 and <= 360
        && (schedule.Exceptions?.Count ?? 0) <= 366
        && (schedule.Exceptions?.All(e => Enum.IsDefined(e.State) && e.State != AvailabilityState.Unknown) ?? true)
        && (schedule.Exceptions?.Select(e => e.Date).Distinct().Count() ?? 0) == (schedule.Exceptions?.Count ?? 0);

    /// <summary>Builds five civil days using real UTC elapsed time, including DST gaps and repetitions.</summary>
    internal static AvailabilityPlannerSnapshot Build(WorldClockSnapshot projection, WorldClockSnapshot current,
        string referenceId, int step, AppSettings settings, CancellationToken cancellationToken)
    {
        var reference = projection.Clocks.First(c => c.CityId == referenceId);
        var referenceZone = TimeZoneInfo.FindSystemTimeZoneById(reference.TimeZoneId);
        var day = DateOnly.FromDateTime(reference.LocalTime.DateTime);
        var first = StartOfDay(day.AddDays(-2), referenceZone);
        var last = StartOfDay(day.AddDays(3), referenceZone);
        // Resolve civil starts only in the selected reference zone. A skipped date may share
        // its resolved instant with a neighboring date; label the real date and draw one line.
        var dayBoundaries = Enumerable.Range(-2, 6)
            .Select(offset => StartOfDay(day.AddDays(offset), referenceZone))
            .Where(instant => instant >= first && instant < last)
            .Distinct()
            .OrderBy(instant => instant)
            .Select(instant => new AvailabilityDayBoundary(instant,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, referenceZone).DateTime)))
            .ToArray();
        var calendar = CelestialCalendarCatalog.Current;
        var holidays = calendar.Holidays.GroupBy(h => (h.Country, h.Date)).ToDictionary(g => g.Key, g => g.ToArray());
        var cities = projection.Clocks.Select(clock =>
        {
            var schedule = settings.AvailabilitySchedules?.GetValueOrDefault(clock.CityId)
                ?? DefaultSchedule(clock.CityId, clock.CityId == settings.HomeCityId, clock.CountryCode);
            return new AvailabilityPlannerCity(
                clock with { Weather = current.Clocks.FirstOrDefault(c => c.CityId == clock.CityId)?.Weather },
                schedule, IsNonWorking(clock.LocalTime, schedule, clock.CountryCode));
        }).ToArray();
        var zones = cities.Select(c => TimeZoneInfo.FindSystemTimeZoneById(c.Clock.TimeZoneId)).ToArray();
        var countries = calendar.Countries.ToDictionary(c => c.Code, StringComparer.Ordinal);
        var rows = new List<AvailabilityRow>();
        for (var instant = first; instant < last; instant = instant.AddMinutes(step))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = instant.AddMinutes(step) < last ? instant.AddMinutes(step) : last;
            var cells = new AvailabilityCell[cities.Length];
            for (var index = 0; index < cities.Length; index++)
            {
                var city = cities[index];
                var local = TimeZoneInfo.ConvertTime(instant, zones[index]);
                var result = Evaluate(local, city.Schedule, city.Clock.CountryCode);
                // A displayed slot is available only if its whole duration is available; rules use minute precision.
                for (var sample = instant.AddMinutes(1); sample < end; sample = sample.AddMinutes(1))
                {
                    var candidate = Evaluate(TimeZoneInfo.ConvertTime(sample, zones[index]), city.Schedule, city.Clock.CountryCode);
                    if (Rank(candidate.State) > Rank(result.State)) result = candidate;
                }
                cells[index] = new(local, TimeZoneInfo.ConvertTime(end, zones[index]), result.State, result.Key, result.Detail,
                    IsNonWorking(local, city.Schedule, city.Clock.CountryCode));
            }
            // UTC slots remain equal elapsed intervals, so a civil boundary can fall inside a row.
            var boundary = dayBoundaries.FirstOrDefault(value => value.InstantUtc >= instant && value.InstantUtc < end);
            rows.Add(new(instant, end, cells, boundary));
        }
        var next = Astronomy.SearchMoonQuarter(new AstroTime(projection.InstantUtc.UtcDateTime));
        return new(referenceId, reference.LocalTime, projection.InstantUtc, current.InstantUtc, step,
            cities, rows, current.WeatherStatus, projection.Map.MoonPhaseAngleDegrees,
            next.quarter, new DateTimeOffset(DateTime.SpecifyKind(next.time.ToUtcDateTime(), DateTimeKind.Utc)));

        bool IsNonWorking(DateTimeOffset local, AvailabilitySchedule schedule, string country)
        {
            holidays.TryGetValue((country, DateOnly.FromDateTime(local.DateTime)), out var events);
            if (events?.Any(e => e.Kind == "holiday") == true) return true;
            if (events?.Any(e => e.Kind == "workday") == true) return false;
            return schedule.RestDays.Contains(local.DayOfWeek) || RegionalWeekend.IsWeekend(country, local.DayOfWeek);
        }

        (AvailabilityState State, string Key, string? Detail) Evaluate(DateTimeOffset local, AvailabilitySchedule schedule, string country)
        {
            var date = DateOnly.FromDateTime(local.DateTime);
            var personal = schedule.Exceptions?.FirstOrDefault(e => e.Date == date);
            if (personal is not null) return (personal.State, "Planner.PersonalException", null);
            var code = country;
            holidays.TryGetValue((code, date), out var events);
            var holiday = events?.FirstOrDefault(h => h.Kind == "holiday");
            if (holiday is not null) return (AvailabilityState.Unavailable, "Planner.Holiday", holiday.Name);
            var recovery = events?.Any(h => h.Kind == "workday") == true;
            if (!recovery && schedule.RestDays.Contains(local.DayOfWeek)) return (AvailabilityState.Unavailable, "Planner.RestDay", null);
            var available = false;
            var reserve = false;
            for (var offset = -1; offset <= 1; offset++)
            {
                var workDate = date.AddDays(offset);
                holidays.TryGetValue((code, workDate), out var workEvents);
                if (workEvents?.Any(h => h.Kind == "holiday") == true) continue;
                if (schedule.RestDays.Contains(workDate.DayOfWeek) && workEvents?.Any(h => h.Kind == "workday") != true) continue;
                var start = workDate.ToDateTime(schedule.Start);
                var finish = workDate.AddDays(schedule.End < schedule.Start ? 1 : 0).ToDateTime(schedule.End);
                available |= local.DateTime >= start && local.DateTime < finish;
                reserve |= local.DateTime >= start.AddMinutes(-schedule.ReserveBeforeMinutes)
                    && local.DateTime < finish.AddMinutes(schedule.ReserveAfterMinutes);
            }
            if (!available && !reserve) return (AvailabilityState.Unavailable, "Planner.OutsideHours", null);
            if (!countries.TryGetValue(code, out var coverage) || date < coverage.CoverageStart || date > coverage.CoverageEnd)
                return (AvailabilityState.Unknown, "Planner.CalendarUnknown", null);
            return available ? (AvailabilityState.Available, "Planner.WorkingHours", null)
                : (AvailabilityState.Reserve, "Planner.Reserve", null);
        }
    }

    /// <summary>Orders conservative slot outcomes without relying on enum serialization values.</summary>
    private static int Rank(AvailabilityState state) => state switch
    { AvailabilityState.Unavailable => 3, AvailabilityState.Unknown => 2, AvailabilityState.Reserve => 1, _ => 0 };

    /// <summary>Resolves a civil boundary to its first real instant, including midnight DST transitions.</summary>
    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (true)
        {
            if (zone.IsInvalidTime(local)) { local = local.AddMinutes(1); continue; }
            var instant = zone.IsAmbiguousTime(local)
                ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
                : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            // Historical date-line changes can map a skipped civil date to the previous
            // real date instead of reporting an invalid wall time. Never start before it.
            if (TimeZoneInfo.ConvertTime(instant, zone).DateTime < local)
            {
                local = local.AddMinutes(1);
                continue;
            }
            return instant;
        }
    }
}
