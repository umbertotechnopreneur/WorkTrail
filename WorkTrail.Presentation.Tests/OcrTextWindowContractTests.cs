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

public sealed class OcrTextWindowContractTests
{
    /// <summary>Guards the restore path against reopening OCR from a replacement gallery selection.</summary>
    [Fact]
    public void OcrTextSession_RestoresOnlyTheRetainedSourceAndPersistsNoExtractedText()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        var contracts = File.ReadAllText(RepositoryFile("WorkTrail.Core", "Application", "Contracts.cs"));
        var restoreStart = source.IndexOf("private async Task RestoreOcrTextWindowAsync", StringComparison.Ordinal);
        var restoreEnd = source.IndexOf("private void ShowOcrTextWindow", restoreStart, StringComparison.Ordinal);
        Assert.True(restoreStart >= 0 && restoreEnd > restoreStart);
        var restore = source[restoreStart..restoreEnd];

        Assert.Contains("bool restoreOcrWindow = false", source, StringComparison.Ordinal);
        Assert.Contains("if (loaded && _restoreOcrSource is { } source", source, StringComparison.Ordinal);
        Assert.Contains("_selectedDetailsState?.OcrText", restore, StringComparison.Ordinal);
        Assert.Contains("StringComparer.OrdinalIgnoreCase.Equals(selected.Path, source.ScreenshotPath)", restore, StringComparison.Ordinal);
        Assert.Contains("selected.CapturedAt == source.CapturedAt", restore, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(ocrText)", restore, StringComparison.Ordinal);
        Assert.Contains("SetWindowOpenStateAsync(WindowStateKeys.OcrText, false", restore, StringComparison.Ordinal);
        Assert.Contains("Screenshots.Error.Unavailable", restore, StringComparison.Ordinal);
        Assert.Contains("SetOcrTextWindowSourceAsync(selected.Path, selected.CapturedAt, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("record OcrTextWindowSource(string ScreenshotPath, DateTimeOffset CapturedAt)", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOcrTextWindowSourceAsync(ocrText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OcrTextWindow_IsASelectableMicaSurfaceWithDebouncedHighlighting()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "OcrTextWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "OcrTextWindow.xaml.cs"));
        var placement = File.ReadAllText(RepositoryFile("WorkTrail", "WindowPlacementService.cs"));
        var search = window.Descendants().Single(element => HasName(element, "SearchBox"));
        var text = window.Descendants().Single(element => HasName(element, "OcrTextBlock"));

        Assert.Contains(window.Descendants(), element =>
            element.Name.LocalName == "MicaBackdrop" && element.Attribute("Kind")?.Value == "BaseAlt");
        Assert.DoesNotContain(window.Descendants(), element => element.Name.LocalName == "Border");
        Assert.Equal("Right", search.Attribute("HorizontalAlignment")?.Value);
        Assert.Contains(search.Descendants(), element =>
            element.Name.LocalName == "FontIcon" && element.Attribute("Glyph")?.Value == "\uE721");
        Assert.Equal("True", text.Attribute("IsTextSelectionEnabled")?.Value);
        Assert.Contains("MinimumQueryLength = 2", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(400)", source, StringComparison.Ordinal);
        Assert.Contains("_searchTimer.IsRepeating = false", source, StringComparison.Ordinal);
        Assert.Contains("OcrTextSearch.FindMatches", source, StringComparison.Ordinal);
        Assert.Contains("OcrTextBlock.TextHighlighters", source, StringComparison.Ordinal);
        Assert.Contains("new SolidColorBrush(Colors.Yellow)", source, StringComparison.Ordinal);
        Assert.Contains("WindowStateKeys.OcrText", source, StringComparison.Ordinal);
        Assert.Contains("public void UpdateContent(string ocrText, ElementTheme theme, string language)", source, StringComparison.Ordinal);
        Assert.Contains("new CustomTitleBarController(", source, StringComparison.Ordinal);
        Assert.Contains("_titleBar.ApplyTheme(theme == ElementTheme.Default ? RootGrid.ActualTheme : theme);", source, StringComparison.Ordinal);
        Assert.Contains("_titleBar.Dispose();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RootGrid_ActualThemeChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyThemeChrome", source, StringComparison.Ordinal);
        Assert.Contains("_xamlRoot.Changed += XamlRoot_Changed", placement, StringComparison.Ordinal);
        Assert.Contains("_xamlRoot.Changed -= XamlRoot_Changed", placement, StringComparison.Ordinal);
        Assert.Contains("_root.Loaded += Root_Loaded", placement, StringComparison.Ordinal);
        Assert.Contains("_root.Loaded -= Root_Loaded", placement, StringComparison.Ordinal);
        Assert.Contains("_placement.Dispose();", source, StringComparison.Ordinal);
        Assert.Contains("RestoreOrCenterAsync(RootGrid, _lifetimeCancellation.Token)", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException) when (_lifetimeCancellation.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("_lifetimeCancellation.Dispose();", source, StringComparison.Ordinal);
        Assert.Contains("sender.Text.Trim().Length < MinimumQueryLength", source, StringComparison.Ordinal);
        Assert.Contains("ApplyHighlights(SearchBox.Text.Trim())", source, StringComparison.Ordinal);

        var textChangedStart = source.IndexOf("private void SearchBox_TextChanged", StringComparison.Ordinal);
        var clearHighlights = source.IndexOf("ClearHighlights();", textChangedStart, StringComparison.Ordinal);
        var restartTimer = source.IndexOf("_searchTimer.Start();", textChangedStart, StringComparison.Ordinal);
        Assert.True(textChangedStart >= 0);
        Assert.True(clearHighlights > textChangedStart);
        Assert.True(restartTimer > clearHighlights);
    }

    private static string RepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkTrail.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            }
        }

        throw new DirectoryNotFoundException("Could not locate the WorkTrail repository root.");
    }

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);
}
