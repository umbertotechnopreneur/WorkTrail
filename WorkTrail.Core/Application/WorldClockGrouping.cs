// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>One visible clock and the selected cities that share its local time.</summary>
public sealed record WorldClockGroup(WorldClockItem Primary, IReadOnlyList<WorldClockItem> Cities);

/// <summary>Projects selected cities into display clocks without changing the saved selection.</summary>
public static class WorldClockGrouping
{
    /// <summary>Places shared offsets first, preserving configured order within and between groups.</summary>
    public static IReadOnlyList<WorldClockGroup> Create(
        IReadOnlyList<WorldClockItem> clocks, bool enabled, string? referenceCityId)
    {
        if (!enabled)
        {
            return clocks.Select(clock => new WorldClockGroup(clock, [clock])).ToArray();
        }

        return clocks
            .GroupBy(clock => clock.LocalTime.Offset)
            .Select(group =>
            {
                var cities = group.ToArray();
                var primary = cities.FirstOrDefault(city => city.CityId == referenceCityId) ?? cities[0];
                return new WorldClockGroup(primary, cities);
            })
            .OrderBy(group => group.Cities.Count == 1)
            .ToArray();
    }
}
