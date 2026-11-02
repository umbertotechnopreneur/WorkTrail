// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Projects civil weekend dates from the city's territory, independently of personal working hours.</summary>
public static class RegionalWeekend
{
    /// <summary>Identifies a local weekend using Unicode CLDR 48 territory conventions.</summary>
    public static bool IsWeekend(string countryCode, DayOfWeek day)
    {
        // Territory exceptions from https://github.com/unicode-org/cldr-json/blob/48.0.0/cldr-json/cldr-core/supplemental/weekData.json.
        // These are calendar conventions, not inferences about a person's religion or work schedule.
        return countryCode.ToUpperInvariant() switch
        {
            "AF" => day is DayOfWeek.Thursday or DayOfWeek.Friday,
            "IR" => day == DayOfWeek.Friday,
            "IN" or "UG" => day == DayOfWeek.Sunday,
            "BH" or "DZ" or "EG" or "IL" or "IQ" or "JO" or "KW" or "LY" or "OM" or "QA" or "SA" or "SD" or "SY" or "YE"
                => day is DayOfWeek.Friday or DayOfWeek.Saturday,
            _ => day is DayOfWeek.Saturday or DayOfWeek.Sunday
        };
    }
}
