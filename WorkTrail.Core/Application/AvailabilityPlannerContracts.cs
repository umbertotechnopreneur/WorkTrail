// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Identifies a source-backed availability result for an entire time slot.</summary>
public enum AvailabilityState { Available, Reserve, Unavailable, Unknown }

/// <summary>Overrides one local calendar day before holiday and weekly rules.</summary>
public sealed record AvailabilityException(DateOnly Date, AvailabilityState State);

/// <summary>Stores local working hours, weekly rest, reserve margins and personal day overrides.</summary>
public sealed record AvailabilitySchedule(string CityId, TimeOnly Start, TimeOnly End,
    IReadOnlyList<DayOfWeek> RestDays, int ReserveBeforeMinutes = 120, int ReserveAfterMinutes = 120,
    IReadOnlyList<AvailabilityException>? Exceptions = null);

/// <summary>Requests five local calendar days around a live or explicitly selected reference.</summary>
public sealed record AvailabilityPlannerRequest(string? ReferenceCityId = null,
    DateTime? ReferenceLocalTime = null, int StepMinutes = 30);

/// <summary>Contains one passive city header with its saved working rules.</summary>
public sealed record AvailabilityPlannerCity(WorldClockItem Clock, AvailabilitySchedule Schedule);

/// <summary>Contains a local time slot and the reason for its availability.</summary>
public sealed record AvailabilityCell(DateTimeOffset LocalStart, DateTimeOffset LocalEnd,
    AvailabilityState State, string ReasonKey, string? Detail = null);

/// <summary>Represents the same UTC interval in all selected cities.</summary>
public sealed record AvailabilityRow(DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    IReadOnlyList<AvailabilityCell> Cells);

/// <summary>Contains the complete planner projection; weather always describes current observations.</summary>
public sealed record AvailabilityPlannerSnapshot(string ReferenceCityId, DateTimeOffset ReferenceLocalTime,
    DateTimeOffset InstantUtc, DateTimeOffset NowUtc, int StepMinutes,
    IReadOnlyList<AvailabilityPlannerCity> Cities, IReadOnlyList<AvailabilityRow> Rows,
    WorldClockWeatherStatus WeatherStatus, double MoonPhaseAngleDegrees,
    int NextMoonQuarter, DateTimeOffset NextMoonPhaseUtc);
