// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class SearchSurfaceContractTests
{
    [Fact]
    public void FloatingSearchWindow_UsesAcrylicAndBoundedScreenshotResults()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "SearchWindow.xaml"));
        var mainWindow = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var windowSource = File.ReadAllText(RepositoryFile("WorkTrail", "SearchWindow.xaml.cs"));
        var viewModelSource = File.ReadAllText(RepositoryFile("WorkTrail.Presentation", "SearchViewModel.cs"));
        var queryBox = window.Descendants().Single(element => HasName(element, "QueryBox"));
        var glow = window.Descendants().Single(element => HasName(element, "SearchActivityGlow"));
        var activity = window.Descendants().Single(element => HasName(element, "SearchActivityStatus"));
        var statusRow = window.Descendants().Single(element => HasName(element, "SearchStatusRow"));
        var root = window.Descendants().Single(element => HasName(element, "RootGrid"));
        var footer = window.Descendants().Single(element => HasName(element, "SearchFooter"));
        var f3Shortcut = mainWindow.Descendants().Single(element =>
            element.Name.LocalName == "KeyboardAccelerator" && element.Attribute("Key")?.Value == "F3");

        Assert.Contains(window.Descendants(), element => element.Name.LocalName == "GlassBackdrop");
        Assert.Equal("MainKeyboardAccelerator_Invoked", f3Shortcut.Attribute("Invoked")?.Value);
        Assert.Equal("48", queryBox.Attribute("MinHeight")?.Value);
        Assert.Equal("Center", queryBox.Attribute("VerticalContentAlignment")?.Value);
        Assert.DoesNotContain(window.Descendants(), element => element.Name.LocalName == "AutoSuggestBox.ItemTemplate");
        Assert.Equal("SearchActivityLine", glow.Name.LocalName);
        Assert.Equal("2", glow.Attribute("Height")?.Value);
        Assert.Null(glow.Attribute("Visibility"));
        Assert.Null(glow.Attribute("SizeChanged"));
        Assert.Equal("Raw", glow.Attribute("AutomationProperties.AccessibilityView")?.Value);
        // Feedback must own an Auto row rather than compete with the star-sized result viewport.
        Assert.Same(statusRow, activity.Parent);
        Assert.Same(statusRow, window.Descendants().Single(element => HasName(element, "EmptyStatePanel")).Parent);
        var statusIndex = int.Parse(statusRow.Attribute("Grid.Row")!.Value);
        var rows = root.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements().ToArray();
        Assert.Equal("Auto", rows[statusIndex].Attribute("Height")?.Value);
        Assert.Contains(activity.Descendants(), element => element.Name.LocalName == "ProgressRing");
        Assert.Contains(activity.Descendants(), element => element.Attribute("AutomationProperties.LiveSetting")?.Value == "Polite");
        Assert.Equal("5", footer.Attribute("Grid.Row")?.Value);
        Assert.Contains(footer.Descendants(), element => HasName(element, "SearchAvailabilityText"));
        Assert.Contains(footer.Descendants(), element => HasName(element, "TextReadingStatusText"));
        Assert.DoesNotContain("IsAlwaysOnTop", windowSource, StringComparison.Ordinal);
        Assert.Contains("presenter.IsResizable = hasResults;", windowSource, StringComparison.Ordinal);
        Assert.Contains("presenter.IsMinimizable = false;", windowSource, StringComparison.Ordinal);
        Assert.Contains("presenter.IsMaximizable = false;", windowSource, StringComparison.Ordinal);
        Assert.Contains("RootGrid.RequestedTheme = ElementTheme.Light;", windowSource, StringComparison.Ordinal);
        Assert.Contains("LogicalWindowWidth = 640", windowSource, StringComparison.Ordinal);
        Assert.Contains("LogicalWindowHeight = 156", windowSource, StringComparison.Ordinal);
        Assert.Contains("_placement.ResizeForContent(RootGrid, _expandedWidth, _expandedHeight)", windowSource, StringComparison.Ordinal);
        Assert.Contains("_placement.ResizeForContent(RootGrid, LogicalWindowWidth, height)", windowSource, StringComparison.Ordinal);
        Assert.Contains("_placement.RestoreOrCenterAsync", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeAndCenterOnCursorDisplay(", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeForCurrentState", windowSource, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(250)", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestAsync", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementCompositionPreview", windowSource, StringComparison.Ordinal);
        Assert.Contains("textBox.IsTextPredictionEnabled = false;", windowSource, StringComparison.Ordinal);
        Assert.Equal(2, windowSource.Split("BeginSearchActivity();", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, windowSource.Split("EndSearchActivity();", StringSplitOptions.None).Length - 1);
        Assert.Contains("public const int MaximumResults = 20;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("MinimumQueryLength = 3", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("IncludeTextContent = true", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("Limit = MaximumResults", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(hit => hit.Score)", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPreview_SeparatesScrollableTextFromTheOpenActionAndUsesCompactRows()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "SearchWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "SearchWindow.xaml.cs"));
        var row = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "SearchResultItemControl.xaml"));
        var rowSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "SearchResultItemControl.xaml.cs"));
        var list = window.Descendants().Single(element => HasName(element, "SearchResultsList"));
        var preview = window.Descendants().Single(element => HasName(element, "PreviewPane"));
        var scroller = window.Descendants().Single(element => HasName(element, "PreviewScroller"));
        var open = window.Descendants().Single(element => HasName(element, "OpenSnapshotButton"));
        var count = window.Descendants().Single(element => HasName(element, "ResultCountText"));

        Assert.Equal("Single", list.Attribute("SelectionMode")?.Value);
        Assert.Equal("False", list.Attribute("IsItemClickEnabled")?.Value);
        Assert.Equal("SearchResultsList_SelectionChanged", list.Attribute("SelectionChanged")?.Value);
        Assert.Equal("SearchResultsList_KeyDown", list.Attribute("KeyDown")?.Value);
        Assert.Equal("1", preview.Attribute("Grid.Column")?.Value);
        Assert.Equal("Auto", scroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", scroller.Attribute("HorizontalScrollMode")?.Value);
        Assert.Same(preview, open.Parent);
        Assert.Equal("1", open.Attribute("Grid.Row")?.Value);
        Assert.DoesNotContain(scroller.Descendants(), element => HasName(element, "OpenSnapshotButton"));
        Assert.Contains(scroller.Descendants(), element => HasName(element, "PreviewBodyText") && element.Attribute("IsTextSelectionEnabled")?.Value == "True");
        Assert.Equal("Polite", count.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.DoesNotContain(row.Descendants(), element => element.Name.LocalName == "Image" || element.Name.LocalName == "ThemeShadow");
        Assert.Contains(row.Descendants(), element => HasName(element, "ResultTitleText"));
        Assert.Contains(row.Descendants(), element => element.Attribute("Text")?.Value == "{Binding SourceDisplay}");
        Assert.Contains("SearchTextHighlight.Apply(control.ResultTitleText", rowSource, StringComparison.Ordinal);
        Assert.Contains("SearchTextHighlight.Apply(PreviewBodyText, result?.PreviewText", source, StringComparison.Ordinal);
        Assert.Contains("SearchResultsList.SelectedItem = _viewModel.SelectedResult;", source, StringComparison.Ordinal);
        Assert.Contains("Search.Results.Limited", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(PreviewPane, stacked ? 1 : 0);", source, StringComparison.Ordinal);
        Assert.Contains("ScreenshotPreviewRequestedEventArgs", source, StringComparison.Ordinal);
        var selectionStart = source.IndexOf("private void SearchResultsList_SelectionChanged", StringComparison.Ordinal);
        var selectionEnd = source.IndexOf("private void RenderSelectedPreview", selectionStart, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenshotRequested", source[selectionStart..selectionEnd], StringComparison.Ordinal);
    }

    /// <summary>Selected screenshots render inline with safe async selection and accessible material fallbacks.</summary>
    [Fact]
    public void InlineScreenshotPreview_UsesFacadeLoadingAndAcrylicWithHighContrastFallbacks()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "SearchWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "SearchWindow.xaml.cs"));
        var image = window.Descendants().Single(element => HasName(element, "PreviewScreenshotImage"));
        var previewHost = window.Descendants().Single(element => HasName(element, "PreviewScreenshotHost"));
        var query = window.Descendants().Single(element => HasName(element, "QueryBox"));
        var results = window.Descendants().Single(element => HasName(element, "SearchResultsPane"));
        var highContrast = window.Descendants().Single(element => KeyValue(element) == "HighContrast");
        var loading = window.Descendants().Single(element => HasName(element, "PreviewImageLoadingPanel"));
        var unavailable = window.Descendants().Single(element => HasName(element, "PreviewImageUnavailableText"));

        Assert.Equal("Uniform", image.Attribute("Stretch")?.Value);
        Assert.Equal("420", image.Attribute("MaxHeight")?.Value);
        Assert.Same(previewHost, image.Parent);
        Assert.Contains(previewHost.ElementsAfterSelf(), element => HasName(element, "PreviewTitleText"));
        Assert.Equal("{ThemeResource SearchQueryBackdropBrush}", query.Attribute("Background")?.Value);
        Assert.Equal("{ThemeResource SearchResultsBackdropBrush}", results.Attribute("Background")?.Value);
        Assert.Equal(2, window.Descendants().Count(element => element.Name.LocalName == "AcrylicBrush"));
        var resultBrushes = window.Descendants().Where(element =>
            element.Name.LocalName == "SolidColorBrush" && KeyValue(element) == "SearchResultsBackdropBrush").ToArray();
        Assert.Equal(2, resultBrushes.Length);
        Assert.All(resultBrushes, brush => Assert.Equal("Transparent", brush.Attribute("Color")?.Value));
        Assert.Equal("Transparent", window.Descendants().Single(element => HasName(element, "SearchResultsList")).Attribute("Background")?.Value);
        Assert.Contains(highContrast.Elements(), element => KeyValue(element) == "SearchQueryBackdropBrush"
            && element.Attribute("ResourceKey")?.Value == "SystemColorWindowColorBrush");
        Assert.Contains(highContrast.Elements(), element => KeyValue(element) == "SearchResultsBackdropBrush"
            && element.Attribute("ResourceKey")?.Value == "SystemColorWindowColorBrush");
        Assert.Contains(loading.Descendants(), element => element.Attribute("Tag")?.Value == "Search.Preview.Loading");
        Assert.Equal("Search.Preview.Unavailable", unavailable.Attribute("Tag")?.Value);
        Assert.Equal("Polite", unavailable.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Contains("new ScreenshotBitmapSourceLoader(application)", source, StringComparison.Ordinal);
        Assert.Contains("_screenshotBitmapLoader.LoadAsync", source, StringComparison.Ordinal);
        Assert.Contains("_previewSelection.IsCurrent(screenshotPath, generation)", source, StringComparison.Ordinal);
        Assert.Contains("_previewSelection.Invalidate();", source, StringComparison.Ordinal);
        Assert.Contains("CancelPreviewImageLoad();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Read", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageFile", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_ExposeSearchPreferencesAndOnlyOcrCanBeDisabled()
    {
        var options = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml.cs"));
        var retention = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml"));
        var retentionSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "RetentionOperationsControl.xaml.cs"));
        var localization = File.ReadAllText(RepositoryFile("WorkTrail", "UiLocalization.cs"));

        Assert.Contains(options.Descendants(), element => HasName(element, "SearchOptionsView"));
        Assert.Contains(options.Descendants(), element => HasName(element, "SearchLanguageBox"));
        Assert.Contains(options.Descendants(), element => HasName(element, "SearchSynonymsSwitch"));
        Assert.Contains(options.Descendants(), element => HasName(element, "SearchTypoToleranceSwitch"));
        Assert.Contains(retention.Descendants(), element => HasName(element, "OcrEnabledSwitch"));
        Assert.DoesNotContain(options.Descendants(), element => HasName(element, "OcrEnabledSwitch"));
        Assert.DoesNotContain(options.Descendants(), element => HasName(element, "SearchEnabledSwitch"));
        Assert.Contains("QueueStorageSave(\"ocr.enabled\"", retentionSource, StringComparison.Ordinal);
        Assert.Contains("QueueAutoSave(", source, StringComparison.Ordinal);
        Assert.Contains("\"search.synonyms\"", source, StringComparison.Ordinal);
        Assert.Contains("SearchIndexingRequested?.Invoke", source, StringComparison.Ordinal);
        Assert.Contains("DeclaredChildren(root)", localization, StringComparison.Ordinal);
        Assert.Contains("case Panel panel:", localization, StringComparison.Ordinal);
        Assert.Contains("case UserControl userControl", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchIndexingWindow_RendersMicaBeforeStartingCancellableFacadeOperation()
    {
        var window = XDocument.Load(RepositoryFile("WorkTrail", "SearchIndexingWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "SearchIndexingWindow.xaml.cs"));
        var mainSource = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var root = window.Descendants().Single(element => HasName(element, "RootGrid"));
        var resultsProgress = window.Descendants().Single(element => HasName(element, "ResultsProgressBar"));

        var backdrop = window.Descendants().Single(element => element.Name.LocalName == "MicaBackdrop");
        Assert.Equal("BaseAlt", backdrop.Attribute("Kind")?.Value);
        Assert.Equal("Transparent", root.Attribute("Background")?.Value);
        Assert.Equal("True", resultsProgress.Attribute("IsIndeterminate")?.Value);
        Assert.DoesNotContain(window.Descendants(), element => HasName(element, "SuggestionsProgressBar"));
        Assert.Contains(window.Descendants(), element => HasName(element, "CancelOrCloseButton"));
        Assert.Contains("_application.RebuildSearchIndexAsync", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, RunIndexingFromVisibleWindow)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await RunIndexingAsync();", source[source.IndexOf("private async void RootGrid_Loaded", StringComparison.Ordinal)..source.IndexOf("private async void RunIndexingFromVisibleWindow", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("_rebuildCancellation?.Cancel()", source, StringComparison.Ordinal);
        Assert.Contains("WindowStateKeys.SearchIndexing", source, StringComparison.Ordinal);
        Assert.Contains("options.SearchIndexingRequested += OptionsControl_SearchIndexingRequested", mainSource, StringComparison.Ordinal);
        Assert.Contains("new SearchIndexingWindow(", mainSource, StringComparison.Ordinal);
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

    private static string? KeyValue(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value;
}
