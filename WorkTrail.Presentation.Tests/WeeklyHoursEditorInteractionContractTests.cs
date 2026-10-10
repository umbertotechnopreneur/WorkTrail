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

/// <summary>Guards the responsive and input contracts of the weekly hours editor.</summary>
public sealed class WeeklyHoursEditorInteractionContractTests
{
    /// <summary>Ensures cells are native empty toggles and no overlay prevents tap or keyboard input.</summary>
    [Fact]
    public void CellsUseNativeToggleInputWithoutAnInteractionOverlayOrInactiveGlyph()
    {
        var editor = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "WeeklyHoursEditor.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "WeeklyHoursEditor.xaml.cs"));

        Assert.DoesNotContain(editor.Descendants(), element => element.Name.LocalName == "Ellipse");
        Assert.DoesNotContain(editor.Descendants(), element => HasName(element, "GridInteractionSurface"));
        Assert.Contains(editor.Descendants(), element => HasName(element, "SelectionIndicator"));
        Assert.Contains("UseSystemFocusVisuals", editor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("IsHitTestVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("PointerDeviceType.Touch", source, StringComparison.Ordinal);
        Assert.Contains("slot.ReleasePointerCapture(e.Pointer)", source, StringComparison.Ordinal);
        Assert.Contains("DaysHost.CapturePointer(e.Pointer)", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("DaysHost.CapturePointer(e.Pointer)", StringComparison.Ordinal)
            < source.IndexOf("private void DaysHost_PointerMoved", StringComparison.Ordinal));
        Assert.Contains("if (!_isDragging && _dragSelectionValue.HasValue)", source, StringComparison.Ordinal);
    }

    /// <summary>Ensures the selection fill follows the combined states used by the WinUI ToggleButton runtime.</summary>
    [Fact]
    public void CellsUseTheWinUiToggleButtonCommonStateContract()
    {
        var editor = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "WeeklyHoursEditor.xaml"));
        var template = editor.Descendants().Single(element =>
            element.Name.LocalName == "ControlTemplate"
            && element.Attribute("TargetType")?.Value == "ToggleButton");
        var stateGroup = template.Descendants().Single(element => element.Name.LocalName == "VisualStateGroup");
        var stateNames = stateGroup.Elements()
            .Where(element => element.Name.LocalName == "VisualState")
            .Select(element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Name").Value)
            .ToArray();

        Assert.Equal("CommonStates", stateGroup.Attributes().Single(attribute => attribute.Name.LocalName == "Name").Value);
        Assert.Contains("Normal", stateNames);
        Assert.Contains("PointerOver", stateNames);
        Assert.Contains("Pressed", stateNames);
        Assert.Contains("Disabled", stateNames);
        Assert.Contains("Checked", stateNames);
        Assert.Contains("CheckedPointerOver", stateNames);
        Assert.Contains("CheckedPressed", stateNames);
        Assert.Contains("CheckedDisabled", stateNames);
        Assert.DoesNotContain("Unchecked", stateNames);
    }

    /// <summary>Ensures both half-day views share one bounded scroller and keep full-day slot identities.</summary>
    [Fact]
    public void HalfDaysShareOneScrollerAndKeepTheWeekAligned()
    {
        var editor = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "WeeklyHoursEditor.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "WeeklyHoursEditor.xaml.cs"));
        var scroller = editor.Descendants().Single(element => HasName(element, "DaysScrollViewer"));
        var editorGrid = editor.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && element.Elements().Any(child => child.Name.LocalName == "ScrollViewer" && HasName(child, "DaysScrollViewer")));
        Assert.Equal("Stretch", scroller.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Disabled", scroller.Attribute("HorizontalScrollMode")?.Value);
        Assert.Equal("2", scroller.Attribute("Grid.Row")?.Value);
        Assert.Equal("Visible", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Null(scroller.Attribute("MaxHeight"));
        var rows = editorGrid.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements().ToArray();
        Assert.Equal("*", rows[2].Attribute("Height")?.Value);
        Assert.Single(editor.Descendants(), element => element.Name.LocalName == "ScrollViewer");
        Assert.Contains(editorGrid.Elements(), element => HasName(element, "DayHeaders"));
        Assert.DoesNotContain(scroller.Descendants(), element => HasName(element, "DayHeaders"));
        Assert.Contains(editor.Descendants(), element => HasName(element, "MorningButton"));
        Assert.Contains(editor.Descendants(), element => HasName(element, "EveningButton"));
        Assert.Contains("SlotHeight = 24d", source, StringComparison.Ordinal);
        Assert.Contains("new ToggleButton[SlotsPerDay]", source, StringComparison.Ordinal);
        Assert.Contains("Visibility.Visible : Visibility.Collapsed", source, StringComparison.Ordinal);
        Assert.Contains("FromSlots(day, GetSelectedSlots(day))", source, StringComparison.Ordinal);
        Assert.Contains("_viewport.GetSlotIndex(row)", source, StringComparison.Ordinal);
        Assert.Contains("timeline.TransformToVisual(DaysHost)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyDragPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyResponsiveLayout", source, StringComparison.Ordinal);
        Assert.DoesNotContain(editor.Descendants(), element => HasKey(element, "ScheduleDayCardStyle"));
        Assert.Contains(editor.Descendants(), element => HasKey(element, "ScheduleWeekendTimelineStyle"));
        Assert.Contains("_viewport.GetBands(GetSelectedSlots(day))", source, StringComparison.Ordinal);
    }

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);

    private static bool HasKey(XElement element, string key) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == key);
}
