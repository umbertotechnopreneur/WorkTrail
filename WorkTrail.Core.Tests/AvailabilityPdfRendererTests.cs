// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Threading;
using WorkTrail.Application;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Checks PDF generation for the displayed grouped timetable and palette.</summary>
public sealed class AvailabilityPdfRendererTests
{
    /// <summary>Grouped cities and selected slots produce valid PDFs in different palettes.</summary>
    [Fact]
    public void GroupedSelectionRendersWithTheChosenPalette()
    {
        var (instant, snapshot) = CreateSnapshot();
        var request = new AvailabilityPdfRequest(snapshot, instant, 16, "en-US", true, true, instant, 0);
        var standard = AvailabilityPdfRenderer.Render(request, AppContext.BaseDirectory, CancellationToken.None);
        var darker = AvailabilityPdfRenderer.Render(request with { Palette = 2 }, AppContext.BaseDirectory, CancellationToken.None);
        Assert.True(standard.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(standard, 0, 5));
        Assert.False(standard.SequenceEqual(darker));
    }
    /// <summary>Every paper size and orientation has the requested PDF page dimensions.</summary>
    [Theory]
    [InlineData(AvailabilityPdfPageSize.A4, 595.28, 841.89)]
    [InlineData(AvailabilityPdfPageSize.A3, 841.89, 1190.55)]
    [InlineData(AvailabilityPdfPageSize.Letter, 612, 792)]
    [InlineData(AvailabilityPdfPageSize.Legal, 612, 1008)]
    public void PaperOptionsProduceCorrectPageDimensions(AvailabilityPdfPageSize size, double shortSide, double longSide)
    {
        var (instant, snapshot) = CreateSnapshot();
        foreach (var orientation in Enum.GetValues<AvailabilityPdfOrientation>())
        {
            byte[]? previous = null;
            foreach (var margins in Enum.GetValues<AvailabilityPdfMargins>())
            {
                var request = new AvailabilityPdfRequest(snapshot, instant, 16, "en-US", true, true, instant)
                {
                    PageSize = size,
                    Orientation = orientation,
                    Margins = margins
                };
                var pdf = AvailabilityPdfRenderer.Render(request, AppContext.BaseDirectory, CancellationToken.None);
                var content = System.Text.Encoding.Latin1.GetString(pdf);
                var box = System.Text.RegularExpressions.Regex.Match(content, @"/MediaBox\s*\[\s*0\s+0\s+(?<w>[\d.]+)\s+(?<h>[\d.]+)\s*\]");
                Assert.True(box.Success, "PDF page size is missing.");
                var width = double.Parse(box.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var height = double.Parse(box.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture);
                Assert.InRange(width, (orientation == AvailabilityPdfOrientation.Landscape ? longSide : shortSide) - 0.51,
                    (orientation == AvailabilityPdfOrientation.Landscape ? longSide : shortSide) + 0.51);
                Assert.InRange(height, (orientation == AvailabilityPdfOrientation.Landscape ? shortSide : longSide) - 0.51,
                    (orientation == AvailabilityPdfOrientation.Landscape ? shortSide : longSide) + 0.51);
                if (previous is not null) Assert.False(previous.SequenceEqual(pdf));
                previous = pdf;
            }
        }
    }

    private static (DateTimeOffset Instant, AvailabilityPlannerSnapshot Snapshot) CreateSnapshot()
    {
        var instant = new DateTimeOffset(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);
        var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
        var clock = new WorldClockItem("hk", "Hong Kong", "HK", "China Standard Time", local, false, null, true,
            null, null, 90, "Assets/WorldClocks/Skylines/hong-kong-summer.png", "summer", new("day", [], []), null);
        var second = clock with { CityId = "kl", CityName = "Kuala Lumpur", CountryCode = "MY", TimeZoneId = "Singapore Standard Time" };
        var clocks = new WorldClockSnapshot(instant, [clock, second], 12,
            new("openweather", "configuration-required", "missing-api-key", 2, 0, false),
            new(new(0, 0), new(0, 0), 90, []));
        var snapshot = AvailabilityPlannerService.Build(clocks, clocks, "hk", 30,
            new AppSettings(HomeCityId: "hk", WorldClockAutoGroupCities: true), CancellationToken.None);
        Assert.Single(snapshot.DisplayGroups!);
        return (instant, snapshot);
    }
}
