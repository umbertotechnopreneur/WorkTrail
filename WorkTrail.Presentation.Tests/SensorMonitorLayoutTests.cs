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
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class SensorMonitorLayoutTests
{
    [Theory]
    [InlineData(476, 148, true)]
    [InlineData(952, 96, false)]
    [InlineData(352, 148, true)]
    [InlineData(280, 148, true)]
    [InlineData(859, 148, true)]
    [InlineData(860, 96, false)]
    [InlineData(1920, 96, false)]
    public void Rows_ReflowWithinTheirAllocatedHeight(double width, double height, bool stacked)
    {
        var layout = SensorMonitorLayout.ResolveTrack(width);
        Assert.Equal(stacked, layout.Stacked);
        Assert.Equal(height, layout.RowHeight);
        Assert.True(layout.Padding * 2 + (stacked ? 124 : 64) < layout.RowHeight);
        if (stacked)
        {
            Assert.Equal(width, layout.NameWidth + 44);
            Assert.Equal(width, layout.ValueWidth + layout.TemperatureWidth + 60);
            Assert.True(layout.ValueWidth >= 110);
            Assert.Equal(layout.ValueWidth, layout.TemperatureWidth);
        }
        else Assert.Equal(width, layout.NameWidth + layout.ValueWidth + layout.TemperatureWidth + 76);
    }

    [Fact]
    public void NormalLaptopViewport_KeepsCpuRamGpuDisksAndBatteryOnOnePage()
    {
        Assert.Equal(7, SensorMonitorLayout.PageSize(700, SensorMonitorLayout.ResolveTrack(1000).RowHeight, 7));
        Assert.Equal(2, SensorMonitorLayout.PageSize(240, 96, 8));
        Assert.Equal(4, SensorMonitorLayout.PageSize(700, 148, 7));
        Assert.Equal(3, SensorMonitorLayout.PageSize(476, 148, 7));
        Assert.Equal(1, SensorMonitorLayout.PageSize(100, 148, 7));
        Assert.Equal(0, SensorMonitorLayout.PageSize(240, 96, 0));
    }

    [Fact]
    public void EnlargedText_UsesMeasuredHeightAndKeepsAnOversizeRowReachable()
    {
        Assert.Equal(1, SensorMonitorLayout.PageSize(476, 280, 7));
        Assert.Equal(2, SensorMonitorLayout.PageSize(700, 280, 7));
        Assert.Equal(1, SensorMonitorLayout.PageSize(100, 280, 7));
    }

    [Fact]
    public void Geometry_RejectsInvalidSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorMonitorLayout.ResolveTrack(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorMonitorLayout.PageSize(-1, 1000, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorMonitorLayout.PageSize(100, double.NaN, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SensorMonitorLayout.PageSize(100, 0, 4));
    }

    [Fact]
    public void Monitor_PagesNormallyButAllowsAnOversizeRowToScrollVertically()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail", "SensorsWindow.xaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var view = XDocument.Load(Path.Combine(directory.FullName, "WorkTrail", "SensorsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var monitor = view.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "MonitorSurface");
        Assert.DoesNotContain(monitor.Descendants(), element => element.Name.LocalName == "Viewbox");
        var viewport = Assert.Single(monitor.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.Equal("Disabled", viewport.Attribute("HorizontalScrollMode")?.Value);
        Assert.Equal("Auto", viewport.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Grid", monitor.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "TracksHost").Name.LocalName);

        var track = XDocument.Load(Path.Combine(directory.FullName, "WorkTrail", "Controls", "SensorTrackControl.xaml"));
        var background = track.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "TraceHost");
        var content = track.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "ContentGrid");
        Assert.Same(content.Parent, background.Parent);
        Assert.DoesNotContain(background.Parent!.Elements(), element => element.Name.LocalName is "Grid.ColumnDefinitions" or "Grid.RowDefinitions");
    }
}
