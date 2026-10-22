// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Suggests a catalog city from the Windows time zone without accessing location or the network.</summary>
internal static class HomeCitySuggestion
{
    /// <summary>Returns a matching city, or no suggestion when the Windows zone has no catalog match.</summary>
    internal static string? Resolve(IReadOnlyList<WorldClockCitySummary> cities, TimeZoneInfo windowsZone)
    {
        return cities.Where(city => Matches(city.TimeZoneId, windowsZone.Id))
            .OrderByDescending(city => city.IsCapital).ThenBy(city => city.Name, StringComparer.Ordinal)
            .Select(city => city.Id).FirstOrDefault();
    }

    private static bool Matches(string catalogZone, string windowsZone)
    {
        if (string.Equals(catalogZone, windowsZone, StringComparison.OrdinalIgnoreCase)) return true;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(catalogZone, out var id)
            && string.Equals(id, windowsZone, StringComparison.OrdinalIgnoreCase);
    }
}
