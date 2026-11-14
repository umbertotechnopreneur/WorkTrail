// SPDX-License-Identifier: MIT
using WorkTrail.Presentation;
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Checks independent home width and shared resizing of the other columns.</summary>
public sealed class TimelineColumnLayoutTests
{
    /// <summary>Automatically fitted columns initially share the available viewport.</summary>
    [Fact]
    public void AutomaticColumnsFitTheViewport()
    {
        var columns = new TimelineColumnLayout();
        columns.SetHomeColumnPresent(true);
        columns.UpdateViewport(1200, 4);
        Assert.Equal(300, columns.GetColumnWidth(0));
        Assert.Equal(300, columns.GetColumnWidth(1));
        Assert.Equal(1200, columns.TotalWidth);
    }

    /// <summary>Moving the first edge changes home alone, including beyond the viewport.</summary>
    [Fact]
    public void HomeEdgeChangesOnlyHome()
    {
        var columns = new TimelineColumnLayout();
        columns.SetHomeColumnPresent(true);
        columns.UpdateViewport(1200, 4);
        columns.ResizeColumn(350, 0);
        Assert.Equal(650, columns.GetColumnWidth(0));
        Assert.Equal(300, columns.GetColumnWidth(1));
        Assert.Equal(1550, columns.TotalWidth);
        Assert.Equal(650, columns.PreferredHomeWidth);
        Assert.Equal(300, columns.PreferredWidth);
    }

    /// <summary>Moving a later edge changes every preceding width while preserving the home difference.</summary>
    [Fact]
    public void LaterEdgeResizesAllColumnsAndFollowsThePointer()
    {
        var columns = new TimelineColumnLayout();
        columns.SetHomeColumnPresent(true);
        columns.UpdateViewport(1200, 4);
        columns.SetPreferredWidths(400, 200);
        var before = columns.GetColumnWidth(0) + 2 * columns.GetColumnWidth(1);
        columns.ResizeColumn(60, 2);
        Assert.Equal(420, columns.GetColumnWidth(0));
        Assert.Equal(220, columns.GetColumnWidth(1));
        Assert.Equal(60, columns.GetColumnWidth(0) + 2 * columns.GetColumnWidth(1) - before);
        Assert.Equal(1080, columns.TotalWidth);
    }

    /// <summary>Fitting home leaves the other widths alone; fitting another edge resets both.</summary>
    [Fact]
    public void FitRespectsTheSelectedEdge()
    {
        var columns = new TimelineColumnLayout();
        columns.SetHomeColumnPresent(true);
        columns.UpdateViewport(1200, 4);
        columns.SetPreferredWidths(500, 200);
        columns.FitColumn(0);
        Assert.Equal(600, columns.GetColumnWidth(0));
        Assert.Equal(200, columns.GetColumnWidth(1));
        columns.FitColumn(2);
        Assert.Equal(300, columns.GetColumnWidth(0));
        Assert.Equal(300, columns.GetColumnWidth(1));
        Assert.Equal(0, columns.PreferredHomeWidth);
        Assert.Equal(0, columns.PreferredWidth);
    }

    /// <summary>Manual widths survive temporary viewport changes and do not shrink below readability.</summary>
    [Fact]
    public void ManualWidthsSurviveViewportChanges()
    {
        var columns = new TimelineColumnLayout();
        columns.SetHomeColumnPresent(true);
        columns.UpdateViewport(1200, 4);
        columns.SetPreferredWidths(400, 200);
        columns.UpdateViewport(300, 4);
        Assert.Equal(400, columns.GetColumnWidth(0));
        Assert.Equal(200, columns.GetColumnWidth(1));
        columns.ResizeColumn(-10000, 2);
        Assert.Equal(340, columns.GetColumnWidth(0));
        Assert.Equal(140, columns.GetColumnWidth(1));
        Assert.Equal(760, columns.TotalWidth);
    }

    /// <summary>When home is absent, the first edge uses shared resizing instead of a phantom home column.</summary>
    [Fact]
    public void WithoutHomeAllColumnsShareTheFirstEdge()
    {
        var columns = new TimelineColumnLayout();
        columns.UpdateViewport(900, 3);
        columns.ResizeColumn(50, 0);
        Assert.Equal(350, columns.GetColumnWidth(0));
        Assert.Equal(350, columns.GetColumnWidth(1));
        Assert.Equal(1050, columns.TotalWidth);
    }
}
