// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System;
using System.IO;
using System.Linq;
using WorkTrail.Application;
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class ActivityWeekProjectionTests
{
    [Fact]
    public void Create_MapsAllHoursToMondayFirstDatesAndDisablesFutureDays()
    {
        var monday = new DateOnly(2026, 9, 7);
        var saturday = monday.AddDays(5);
        var snapshot = Snapshot(monday, saturday);
        var cells = ActivityWeekProjection.Create(snapshot, monday, monday, saturday);
        Assert.Equal(168, cells.Count);
        Assert.Equal(monday, cells[0].Date);
        Assert.Equal(0, cells[0].Hour);
        Assert.Equal(monday.AddDays(6), cells[^1].Date);
        Assert.Equal(23, cells[^1].Hour);
        Assert.Equal(24, cells.Count(cell => !cell.IsAvailable));
        Assert.Equal(168, cells.Select(cell => (cell.Date, cell.Hour)).Distinct().Count());
    }

    [Fact]
    public void Create_PreservesRecordedZeroAndRejectsDuplicateOrAveragedBuckets()
    {
        var monday = new DateOnly(2026, 9, 7);
        var snapshot = Snapshot(monday, monday.AddDays(6));
        var hours = snapshot.HourOfWeek.ToArray();
        var index = Array.FindIndex(hours, cell => cell.DayOfWeek == 1 && cell.Hour == 10);
        var profile = InstallationProfileCatalog.CreateDefault("0123456789abcdef0123456789abcdef", "PC", DateTimeOffset.UnixEpoch);
        hours[index] = hours[index] with { HasData = true, ObservationDays = 1, SampleCount = 1, TrackedSeconds = 60, IdleSeconds = 60, ActivityScore = 0, Installations = [profile] };
        snapshot = snapshot with { HourOfWeek = hours };
        var cell = ActivityWeekProjection.Create(snapshot, monday, monday, monday.AddDays(6)).Single(cell => cell.Date == monday && cell.Hour == 10);
        Assert.Equal(0, cell.Activity.ActivityScore);
        Assert.Equal(profile, Assert.Single(cell.Activity.Installations));
        hours[index] = hours[index] with { ObservationDays = 2 };
        Assert.Throws<InvalidDataException>(() => ActivityWeekProjection.Create(snapshot, monday, monday, monday.AddDays(6)));
        hours[index] = hours[0];
        Assert.Throws<InvalidDataException>(() => ActivityWeekProjection.Create(snapshot, monday, monday, monday.AddDays(6)));
    }

    [Fact]
    public void Create_RejectsMissingDuplicateAndUnrecordedInstallationProvenance()
    {
        var monday = new DateOnly(2026, 9, 7);
        var snapshot = Snapshot(monday, monday.AddDays(6));
        var hours = snapshot.HourOfWeek.ToArray();
        var index = Array.FindIndex(hours, cell => cell.DayOfWeek == 1 && cell.Hour == 10);
        var profile = InstallationProfileCatalog.CreateDefault("0123456789abcdef0123456789abcdef", "PC", DateTimeOffset.UnixEpoch);
        var recorded = hours[index] with { HasData = true, ObservationDays = 1, SampleCount = 1, ActivityScore = 0 };
        snapshot = snapshot with { HourOfWeek = hours };
        foreach (var invalid in new[]
        {
            recorded with { Installations = [] },
            recorded with { Installations = null! },
            recorded with { Installations = [profile, profile] },
            recorded with { Installations = [profile with { Icon = "invalid" }] },
            hours[index] with { Installations = [profile] }
        })
        {
            hours[index] = invalid;
            Assert.Throws<InvalidDataException>(() => ActivityWeekProjection.Create(snapshot, monday, monday, monday.AddDays(6)));
        }
    }

    private static ReportSnapshot Snapshot(DateOnly from, DateOnly to) => new(
        6, new ReportRange(from, to, "UTC", to.DayNumber - from.DayNumber + 1),
        new ReportTotals(0, 0, 0, 0, 0, 0), [],
        Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour => new ReportHourCell(day, hour, 0, 0, 0, 0, false, 0, 0, 0, null, []))).ToArray(),
        [], [], new ReportDataQuality(false, null, null, 0, 0, 0, 0), AiUsageSummary.Empty);
}
