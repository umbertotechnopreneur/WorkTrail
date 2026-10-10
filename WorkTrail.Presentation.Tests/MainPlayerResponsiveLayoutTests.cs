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

public sealed class MainPlayerResponsiveLayoutTests
{
    [Theory]
    [InlineData(414d, false)]
    [InlineData(520d, false)]
    [InlineData(943d, false)]
    [InlineData(944d, true)]
    [InlineData(1400d, true)]
    public void Width_ReflowsOnlyWhenBothColumnsHaveRoom(double width, bool expected)
    {
        Assert.Equal(expected, MainPlayerResponsiveLayout.UsesColumns(width));
    }

    [Theory]
    [InlineData(500d, MainPlayerDetailLevel.Full)]
    [InlineData(400d, MainPlayerDetailLevel.Full)]
    [InlineData(399d, MainPlayerDetailLevel.Compact)]
    [InlineData(240d, MainPlayerDetailLevel.Compact)]
    [InlineData(239d, MainPlayerDetailLevel.Summary)]
    public void Height_DisclosesMeasuredDetailsWithoutChangingExpansionPreferences(double height, MainPlayerDetailLevel expected)
    {
        var state = new MainWindowLayoutState();
        state.SetSectionVisibility(MainWindowLayoutSection.LastSession, true);

        Assert.Equal(expected, MainPlayerResponsiveLayout.ResolveDetails(height, 400d, 240d));
        Assert.True(state.IsLastSessionVisible);
        Assert.True(state.IsActivityScoreVisible);
        Assert.Equal(MainPlayerDetailLevel.Full, MainPlayerResponsiveLayout.ResolveDetails(500d, 400d, 240d));
    }

    [Theory]
    [InlineData(300d, 400d, 54d)]
    [InlineData(400d, 400d, 54d)]
    [InlineData(460d, 400d, 114d)]
    [InlineData(900d, 400d, 180d)]
    public void Chart_UsesSpareHeightWithABoundedMaximum(double viewport, double content, double expected)
    {
        Assert.Equal(expected, MainPlayerResponsiveLayout.ResolveChartHeight(viewport, content, 54d));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Geometry_RejectsInvalidBounds(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MainPlayerResponsiveLayout.UsesColumns(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainPlayerResponsiveLayout.ResolveDetails(value, 400d, 240d));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainPlayerResponsiveLayout.ResolveChartHeight(400d, value, 54d));
    }

    [Fact]
    public void Player_HasNoScrollContainerAndFitsBelowTheTitlebar()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail", "MainWindow.xaml")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var view = XDocument.Load(Path.Combine(directory.FullName, "WorkTrail", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var player = view.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "PlayerPanel");
        Assert.DoesNotContain(player.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        var fit = player.Descendants().Single(element => element.Name.LocalName == "Viewbox");
        Assert.Equal("DownOnly", fit.Attribute("StretchDirection")?.Value);
        var sensors = view.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "SensorsButton");
        Assert.Equal("Sensors.Open", sensors.Attribute("Tag")?.Value);
        Assert.Equal(sensors.Attribute("ToolTipService.ToolTip")?.Value, sensors.Attribute("AutomationProperties.Name")?.Value);
    }
}
