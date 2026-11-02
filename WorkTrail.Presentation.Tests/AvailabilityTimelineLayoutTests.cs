// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Prevents independently centered city headings and timetable rows.</summary>
public sealed class AvailabilityTimelineLayoutTests
{
    /// <summary>Headers and cells must share one origin even when a wide window leaves spare space.</summary>
    [Fact]
    public void HeadersAndRowsShareALeftAlignedScrollingSurface()
    {
        var document = LoadTimeline();
        var headers = Named(document, "CityHeaders");
        var rows = Named(document, "TimelineRows");
        var surface = Named(document, "TimelineSurface");
        var scroller = Named(document, "TimelineScroller");

        Assert.Same(surface, headers.Parent);
        Assert.Same(surface, rows.Parent);
        Assert.Same(scroller, surface.Parent);
        Assert.Equal("Left", surface.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Left", scroller.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Stretch", scroller.Attribute("VerticalContentAlignment")?.Value);
        Assert.Null(rows.Attribute("Width"));
        Assert.Null(headers.Attribute("Width"));
        Assert.Equal("Stretch", rows.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("0", rows.Attribute("Padding")?.Value);
        Assert.Equal("Disabled", rows.Attribute("ScrollViewer.HorizontalScrollMode")?.Value);
        Assert.Equal("1", rows.Attribute("Grid.Row")?.Value);
    }

    /// <summary>The UTC column stays outside horizontal scrolling and starts at the rows' top edge.</summary>
    [Fact]
    public void UtcGutterStaysFixedOutsideCityScrolling()
    {
        var document = LoadTimeline();
        var gutter = Named(document, "GutterScroller");
        var scroller = Named(document, "TimelineScroller");

        Assert.DoesNotContain(scroller, gutter.Ancestors());
        Assert.Same(scroller.Parent, gutter.Parent?.Parent);
        Assert.Equal("1", gutter.Attribute("Grid.Row")?.Value);
        Assert.Equal("Top", gutter.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Hidden", gutter.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("False", gutter.Attribute("IsHitTestVisible")?.Value);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name);

    private static XDocument LoadTimeline()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx"))) directory = directory.Parent;
        return XDocument.Load(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found."),
            "WorkTrail", "Controls", "AvailabilityTimelineGrid.xaml"));
    }
}
