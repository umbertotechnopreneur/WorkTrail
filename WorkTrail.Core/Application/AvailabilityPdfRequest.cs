// SPDX-License-Identifier: MIT
namespace WorkTrail.Application;

/// <summary>Freezes the displayed planner data and visible interval for a vector PDF export.</summary>
public sealed record AvailabilityPdfRequest(AvailabilityPlannerSnapshot Snapshot, DateTimeOffset StartUtc,
    int Hours, string Language);
