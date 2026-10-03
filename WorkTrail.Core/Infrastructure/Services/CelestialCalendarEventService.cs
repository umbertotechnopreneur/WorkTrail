// SPDX-License-Identifier: MIT

using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Places offline calendar observances in the same city-local celestial event timeline.</summary>
internal static class CelestialCalendarEventService
{
    internal static IReadOnlyList<CelestialAgendaEvent> Build(
        CelestialSnapshot snapshot, TimeZoneInfo zone,
        IReadOnlyList<CelestialCalendarHoliday> holidays,
        IReadOnlyList<CelestialCalendarSaint> saints,
        IReadOnlyList<string> selectedCountries, bool showSaints,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(zone);
        var selected = selectedCountries.ToHashSet(StringComparer.Ordinal);
        var endUtc = snapshot.InstantUtc.AddDays(2);
        var startDate = DateOnly.FromDateTime(snapshot.LocalTime.DateTime);
        var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(endUtc, zone).DateTime);
        var events = new List<CelestialAgendaEvent>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var national = holidays.Where(holiday => holiday.Date == date && holiday.Kind == "holiday"
                && selected.Contains(holiday.Country)).ToArray();
            var dailySaints = showSaints
                ? saints.Where(saint => saint.Month == date.Month && saint.Day == date.Day).ToArray()
                : [];
            if (national.Length == 0 && dailySaints.Length == 0)
            {
                continue;
            }

            var anchor = AtNineLocal(date, zone);
            if (national.Length > 0 && anchor < endUtc)
            {
                // National calendars can overlap; the agenda renders one representative holiday at the shared local time.
                var holiday = national
                    .OrderBy(item => item.ArtworkFileName, StringComparer.Ordinal)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .ThenBy(item => item.Country, StringComparer.Ordinal)
                    .First();
                events.Add(new CelestialAgendaEvent(CelestialEventKind.Holiday, anchor, null,
                    TimeZoneInfo.ConvertTime(anchor, zone), null,
                    CalendarCountryCode: holiday.Country, CalendarLabel: holiday.Name,
                    CalendarQuality: holiday.Quality, CalendarArtworkFileName: holiday.ArtworkFileName,
                    CalendarDate: date));
            }

            foreach (var saint in dailySaints)
            {
                if (anchor >= endUtc) continue;
                events.Add(new CelestialAgendaEvent(CelestialEventKind.Saint, anchor, null,
                    TimeZoneInfo.ConvertTime(anchor, zone), null,
                    CalendarLabel: saint.NameLatin, CalendarEntryKey: saint.EventKey, CalendarDate: date));
            }
        }

        return events;
    }

    private static DateTimeOffset AtNineLocal(DateOnly date, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(9, 0)), zone));
}
