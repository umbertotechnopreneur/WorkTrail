// SPDX-License-Identifier: MIT
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Protects equal city widths and resize geometry independently of native pointer handling.</summary>
public sealed class TimelineColumnLayoutTests
{
    /// <summary>Automatic columns fill a growing viewport without requiring a stored manual width.</summary>
    [Fact]
    public void AutomaticColumnsFollowViewportChanges()
    {
        var columns = new TimelineColumnLayout();
        columns.UpdateViewport(1200, 5);
        Assert.Equal(240, columns.ColumnWidth);
        Assert.Equal(1200, columns.TotalWidth);

        columns.UpdateViewport(1600, 5);
        Assert.Equal(320, columns.ColumnWidth);
        Assert.Equal(1600, columns.TotalWidth);
        Assert.Equal(0, columns.PreferredWidth);
    }

    /// <summary>A temporary narrower viewport constrains geometry without erasing an existing manual preference.</summary>
    [Fact]
    public void SavedWidthSurvivesInitialMeasurementAndTemporaryConstraint()
    {
        var columns = new TimelineColumnLayout();
        columns.SetPreferredWidth(220);
        columns.UpdateViewport(1200, 4);
        Assert.Equal(220, columns.ColumnWidth);

        columns.UpdateViewport(600, 4);
        Assert.Equal(150, columns.ColumnWidth);
        Assert.Equal(220, columns.PreferredWidth);
        columns.SetPreferredWidth(220);
        Assert.Equal(150, columns.ColumnWidth);
        Assert.Equal(220, columns.PreferredWidth);

        columns.UpdateViewport(1400, 4);
        Assert.Equal(220, columns.ColumnWidth);
        columns.FitColumns();
        Assert.Equal(0, columns.PreferredWidth);
        Assert.Equal(350, columns.ColumnWidth);
    }

    /// <summary>Dragging an internal boundary moves that edge by the pointer delta while every column stays equal.</summary>
    [Fact]
    public void InternalBoundaryDeltaIsDistributedAcrossPrecedingColumns()
    {
        var columns = new TimelineColumnLayout();
        columns.UpdateViewport(1000, 4);
        columns.SetPreferredWidth(200);
        var boundaryBefore = columns.ColumnWidth * 3;

        columns.ResizeColumn(60, 2);

        Assert.Equal(220, columns.ColumnWidth);
        Assert.Equal(60, columns.ColumnWidth * 3 - boundaryBefore);
        Assert.Equal(880, columns.TotalWidth);
        columns.ResizeColumn(10000, 2);
        Assert.Equal(250, columns.ColumnWidth);
        Assert.Equal(1000, columns.TotalWidth);
        Assert.Equal(0, columns.PreferredWidth);
        columns.UpdateViewport(1200, 4);
        Assert.Equal(300, columns.ColumnWidth);
        Assert.Equal(1200, columns.TotalWidth);
    }

    /// <summary>Too many cities retain readable widths and expose an overflow extent for horizontal scrolling.</summary>
    [Fact]
    public void InsufficientSpaceKeepsReadableMinimumInsteadOfCompressingCities()
    {
        var columns = new TimelineColumnLayout();
        columns.UpdateViewport(300, 4);
        Assert.Equal(140, columns.ColumnWidth);
        Assert.Equal(560, columns.TotalWidth);

        columns.ResizeColumn(-1000, 0);
        Assert.Equal(140, columns.ColumnWidth);
        columns.ResizeColumn(1000, 3);
        Assert.Equal(140, columns.ColumnWidth);
    }
}
