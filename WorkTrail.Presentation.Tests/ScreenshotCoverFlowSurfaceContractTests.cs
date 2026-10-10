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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace WorkTrail.Presentation.Tests;

public sealed class ScreenshotCoverFlowSurfaceContractTests
{
    [Fact]
    public void ScreenshotHeader_RendersSelectedInstallationProvenanceWithAccessibleAppearance()
    {
        var header = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotHeaderControl.xaml"));
        var headerSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotHeaderControl.xaml.cs"));
        var windowSource = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        var badge = header.Descendants().Single(element => HasName(element, "InstallationProvenanceBadge"));
        var iconBadge = header.Descendants().Single(element => HasName(element, "InstallationIconBadge"));
        var icon = header.Descendants().Single(element => HasName(element, "InstallationIcon"));

        Assert.Equal("Collapsed", badge.Attribute("Visibility")?.Value);
        Assert.Equal("Polite", badge.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Equal("Raw", icon.Attribute("AutomationProperties.AccessibilityView")?.Value);
        Assert.Contains(header.Descendants(), element => HasName(element, "InstallationFriendlyNameText"));
        Assert.Contains(header.Descendants(), element => HasName(element, "InstallationMachineNameText"));
        Assert.Equal("2", iconBadge.Attribute("BorderThickness")?.Value);
        Assert.Contains("InstallationAppearance.CreateAccentBrush(installation.Color)", headerSource, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.GetIconGlyph(installation.Icon)", headerSource, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(InstallationProvenanceBadge", headerSource, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(InstallationProvenanceBadge", headerSource, StringComparison.Ordinal);
        Assert.Contains("var installation = item.Installation", windowSource, StringComparison.Ordinal);
        Assert.Contains("installation);", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotDetails_RendersInstallationProvenanceWithValidatedAppearance()
    {
        var details = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml"));
        var detailsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml.cs"));

        Assert.Contains(details.Descendants(), element => HasName(element, "InstallationBadge"));
        Assert.Contains(details.Descendants(), element => HasName(element, "InstallationIcon"));
        Assert.Contains(details.Descendants(), element => HasName(element, "InstallationNameValueText"));
        Assert.Contains(details.Descendants(), element => HasName(element, "InstallationMachineValueText"));
        Assert.Contains("InstallationProfileCatalog.Colors", detailsSource, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.CreateAccentBrush", detailsSource, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.GetIconGlyph", detailsSource, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName", detailsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotDetails_UsesCurrentDailyGateForTheLocalizedEmptyDescription()
    {
        var details = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml"));
        var windowSource = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        var detailsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml.cs"));
        var emptyDescription = details.Descendants().Single(element => HasName(element, "NoAiDescriptionText"));

        Assert.Equal("Screenshots.AiDescription.Empty", emptyDescription.Attribute("Tag")?.Value);
        Assert.Contains("_application.GetAiStatusAsync(cancellationToken)", windowSource, StringComparison.Ordinal);
        Assert.Contains("CostGate: { Allowed: false, Reason: \"daily_limit\" }", windowSource, StringComparison.Ordinal);
        Assert.Contains("DefaultAiDescriptionEmptyMessageKey = \"Screenshots.AiDescription.Empty\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("DailyAiLimitEmptyMessageKey = \"Notification.AiDailyLimitReached.Message\"", windowSource, StringComparison.Ordinal);
        Assert.Contains("UiLocalization.Apply(DetailsSection, _strings);", windowSource, StringComparison.Ordinal);
        Assert.Contains("_strings.Translate(_aiDescriptionEmptyMessageKey)", windowSource, StringComparison.Ordinal);
        Assert.Contains("NoAiDescriptionText.Text = emptyAiDescriptionText;", detailsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IAiAnalysisService", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalyzeCapturedScreenAsync", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalStore", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotDetails_OpensAvailableOcrTextInOneOwnedWindow()
    {
        var details = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml"));
        var detailsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotDetailsControl.xaml.cs"));
        var windowSource = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        var ocrSection = details.Descendants().Single(element => HasName(element, "OcrTextSection"));
        var openButton = details.Descendants().Single(element => HasName(element, "OpenOcrTextButton"));

        Assert.Equal("Collapsed", ocrSection.Attribute("Visibility")?.Value);
        Assert.Equal("Button", openButton.Name.LocalName);
        Assert.Equal("Transparent", openButton.Attribute("Background")?.Value);
        Assert.Equal("Transparent", openButton.Attribute("BorderBrush")?.Value);
        Assert.Equal("0", openButton.Attribute("BorderThickness")?.Value);
        Assert.Equal("Screenshots.OcrText.Details.Open", openButton.Attribute("Tag")?.Value);
        Assert.Equal("OpenOcrTextButton_Click", openButton.Attribute("Click")?.Value);
        Assert.Equal("Open OCR text in a new window", openButton.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("Open OCR text in a new window", openButton.Attribute("ToolTipService.ToolTip")?.Value);
        Assert.Contains(openButton.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Tag")?.Value == "Screenshots.OcrText.Details.Action"
            && element.Attribute("Text")?.Value == "Open OCR text");
        Assert.Contains(openButton.Descendants(), element =>
            element.Name.LocalName == "FontIcon"
            && element.Attribute("Glyph")?.Value == "\uE8A7");
        Assert.Contains("public event Action<string>? OcrTextRequested;", detailsSource, StringComparison.Ordinal);
        Assert.Contains("_ocrText = string.IsNullOrWhiteSpace(state?.OcrText) ? null : state.OcrText;", detailsSource, StringComparison.Ordinal);
        Assert.Contains("OcrTextSection.Visibility = _ocrText is null ? Visibility.Collapsed : Visibility.Visible;", detailsSource, StringComparison.Ordinal);
        Assert.Contains("OcrTextRequested?.Invoke(ocrText);", detailsSource, StringComparison.Ordinal);
        Assert.Contains("DetailsSection.OcrTextRequested += DetailsSection_OcrTextRequested;", windowSource, StringComparison.Ordinal);
        Assert.Contains("_ocrTextWindow = new OcrTextWindow(", windowSource, StringComparison.Ordinal);
        Assert.Contains("var requestedTheme = RootGrid.RequestedTheme;", windowSource, StringComparison.Ordinal);
        Assert.Contains("_ocrTextWindow.UpdateContent(ocrText, requestedTheme, _strings.Language);", windowSource, StringComparison.Ordinal);
        Assert.Contains("_ocrTextWindow.Activate();", windowSource, StringComparison.Ordinal);
        Assert.Contains("_ocrTextWindow.Closed += OcrTextWindow_Closed;", windowSource, StringComparison.Ordinal);
        Assert.Contains("DetailsSection.OcrTextRequested -= DetailsSection_OcrTextRequested;", windowSource, StringComparison.Ordinal);
        Assert.Contains("ocrTextWindow.Close();", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenshotGallery_UsesSingleZoomableViewerAndVirtualizedTimeline()
    {
        var gallery = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotGalleryViewControl.xaml"));
        var viewer = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotImageViewerControl.xaml"));
        var header = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotHeaderControl.xaml"));
        var timeline = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotTimelineControl.xaml"));
        var chrome = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotChromeStyles.xaml"));
        var app = XDocument.Load(RepositoryFile("WorkTrail", "App.xaml"));
        var dayOverview = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "ScreenshotDayOverviewControl.xaml"));
        var window = XDocument.Load(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml"));
        var viewerSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotImageViewerControl.xaml.cs"));
        var bitmapLoaderSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotBitmapSourceLoader.cs"));
        var gallerySource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotGalleryViewControl.xaml.cs"));
        var timelineSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotTimelineControl.xaml.cs"));
        var dayOverviewSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "ScreenshotDayOverviewControl.xaml.cs"));
        var windowSource = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));
        var imageScroller = viewer.Descendants().Single(element => HasName(element, "ImageScroller"));
        var screenshotImage = viewer.Descendants().Single(element => HasName(element, "ScreenshotImage"));
        var imageHost = viewer.Descendants().Single(element => HasName(element, "ImageHost"));
        var screenshotFrame = viewer.Descendants().Single(element => HasName(element, "ScreenshotFrame"));
        var toolbar = header.Descendants().Single(element => HasName(element, "ScreenshotToolbar"));
        var metadataPanel = header.Descendants().Single(element => HasName(element, "MetadataPanel"));
        var filmstripList = timeline.Descendants().Single(element => HasName(element, "FilmstripList"));
        var filmstripStrip = timeline.Descendants().Single(element => HasName(element, "FilmstripStrip"));
        var timelineCardRoot = timeline.Descendants().Single(element => HasName(element, "TimelineCardRoot"));
        var timelineThumbnailVisual = timeline.Descendants().Single(element => HasName(element, "TimelineThumbnailVisual"));
        var timelineThumbnailSlot = timelineCardRoot.Descendants().First(element =>
            element.Name.LocalName == "RowDefinition" && element.Attribute("Height")?.Value == "69");
        var timelineImage = timeline.Descendants().Single(element => HasName(element, "TimelineImage"));
        var timelineImageFrame = timelineImage.Ancestors().First(element => element.Name.LocalName == "Border");
        var timelineSelectionGlow = timeline.Descendants().Single(element => HasName(element, "TimelineSelectionGlow"));
        var timelineSelectionChrome = timeline.Descendants().Single(element => HasName(element, "TimelineSelectionChrome"));
        var timelineInstallationBadge = timeline.Descendants().Single(element => HasName(element, "TimelineInstallationBadge"));
        var timelineInstallationIcon = timeline.Descendants().Single(element => HasName(element, "TimelineInstallationIcon"));
        var timelineClockIcon = timeline.Descendants().Single(element =>
            element.Name.LocalName == "FontIcon" && element.Attribute("Glyph")?.Value == "\uE823");
        var timelineTimeText = timeline.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding TimeText}");
        var filmstripSurface = timeline.Descendants().Single(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Background")?.Value == "{ThemeResource ScreenshotFilmstripBackdropBrush}");
        var gallerySection = window.Descendants().Single(element => HasName(element, "GallerySection"));
        var detailsPane = window.Descendants().Single(element => HasName(element, "DetailsPane"));
        var markerCanvas = dayOverview.Descendants().Single(element => HasName(element, "MarkerCanvas"));
        var selectionCanvas = dayOverview.Descendants().Single(element => HasName(element, "SelectionCanvas"));
        var rangeLabelCanvas = dayOverview.Descendants().Single(element => HasName(element, "RangeLabelCanvas"));
        var activityBaseline = dayOverview.Descendants().Single(element => HasName(element, "ActivityBaseline"));

        Assert.Contains(gallery.Descendants(), element => element.Name.LocalName == "ScreenshotImageViewerControl");
        Assert.DoesNotContain(gallery.Descendants(), element => element.Name.LocalName == "ScreenshotCoverFlowControl");
        Assert.DoesNotContain(gallery.Descendants(), element => HasName(element, "PreviousPreviewFrame") || HasName(element, "NextPreviewFrame"));
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "ScrollViewer" && element.Attribute("ZoomMode")?.Value == "Enabled");
        Assert.Contains(viewer.Descendants(), element => element.Name.LocalName == "ScrollViewer" && element.Attribute("MaxZoomFactor")?.Value == "5");
        Assert.Equal("Hidden", imageScroller.Attribute("HorizontalScrollBarVisibility")?.Value);
        Assert.Equal("Auto", imageScroller.Attribute("HorizontalScrollMode")?.Value);
        Assert.Equal("Hidden", imageScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Auto", imageScroller.Attribute("VerticalScrollMode")?.Value);
        Assert.Equal("Uniform", screenshotImage.Attribute("Stretch")?.Value);
        Assert.Null(screenshotImage.Attribute("ImageFailed"));
        Assert.Equal("ScreenshotImage_ImageOpened", screenshotImage.Attribute("ImageOpened")?.Value);
        Assert.Equal("ImageHost_PointerWheelChanged", imageHost.Attribute("PointerWheelChanged")?.Value);
        Assert.Equal("Transparent", screenshotFrame.Attribute("Background")?.Value);
        Assert.Null(screenshotFrame.Attribute("Padding"));
        Assert.Null(screenshotFrame.Attribute("BorderBrush"));
        Assert.Null(screenshotFrame.Attribute("BorderThickness"));
        Assert.Null(screenshotFrame.Attribute("CornerRadius"));
        Assert.Equal("0,0,32", screenshotFrame.Attribute("Translation")?.Value);
        Assert.Equal("Collapsed", screenshotFrame.Attribute("Visibility")?.Value);
        Assert.Contains(screenshotFrame.Descendants(), element => element.Name.LocalName == "ThemeShadow");
        Assert.DoesNotContain(viewer.Descendants(), element => HasKey(element, "ScreenshotFrameBorderBrush"));
        Assert.DoesNotContain(viewer.Descendants(), element =>
            element.Name.LocalName is "Button" or "ToggleButton" or "AppBarButton" or "AppBarToggleButton" or "CommandBar");
        Assert.DoesNotContain(viewer.Descendants(), element => HasName(element, "ZoomRail") || HasName(element, "MetadataPanel"));
        Assert.DoesNotContain(viewer.Descendants(), element =>
            element.Name.LocalName == "VisualState" && element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.StartsWith("Overlay", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(gallery.Descendants(), element => HasName(element, "MetadataPanel"));
        Assert.DoesNotContain(gallery.Descendants(), element =>
            HasName(element, "MetadataDateValueText")
            || HasName(element, "MetadataTimeValueText")
            || HasName(element, "MetadataAppValueText"));
        Assert.DoesNotContain(gallery.Descendants(), element => HasKey(element, "ScreenshotMetadataChipStyle"));
        Assert.DoesNotContain("OverlayVisibilityChanged", viewerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayVisibilityChanged", gallerySource, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayVisibilityChanged", windowSource, StringComparison.Ordinal);
        Assert.Equal("CommandBar", toolbar.Name.LocalName);
        Assert.Equal("Transparent", toolbar.Attribute("Background")?.Value);
        Assert.Equal("0", toolbar.Attribute("BorderThickness")?.Value);
        Assert.Equal("1", toolbar.Attribute("Grid.Column")?.Value);
        Assert.Equal("Collapsed", metadataPanel.Attribute("Visibility")?.Value);
        Assert.Equal(3, metadataPanel.Descendants().Count(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Style")?.Value == "{StaticResource ScreenshotMetadataPillBorderStyle}"));
        Assert.DoesNotContain(metadataPanel.Descendants(), element => element.Name.LocalName == "ThemeShadow");
        Assert.Contains(header.Descendants(), element => HasName(element, "MetadataDateValueText"));
        Assert.Contains(header.Descendants(), element => HasName(element, "MetadataTimeValueText"));
        Assert.Contains(header.Descendants(), element => HasName(element, "MetadataAppValueText"));
        Assert.Contains(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary"
            && element.Attribute("Source")?.Value == "Controls/ScreenshotChromeStyles.xaml");
        Assert.Contains(chrome.Descendants(), element =>
            element.Name.LocalName == "StaticResource"
            && HasKey(element, "ScreenshotSelectionBorderBrush")
            && element.Attribute("ResourceKey")?.Value == "SystemColorHighlightTextColorBrush");
        Assert.Equal("FilmstripList_ContainerContentChanging", filmstripList.Attribute("ContainerContentChanging")?.Value);
        Assert.Contains(timeline.Descendants(), element => element.Name.LocalName == "ItemsStackPanel" && element.Attribute("Orientation")?.Value == "Horizontal");
        Assert.Contains(timeline.Descendants(), element => HasName(element, "PreviousTimelineButton"));
        Assert.Contains(timeline.Descendants(), element => HasName(element, "NextTimelineButton"));
        Assert.Contains(timeline.Descendants(), element =>
            element.Name.LocalName == "Image" && element.Attribute("Stretch")?.Value == "Uniform");
        Assert.Null(timelineImage.Attribute("ImageFailed"));
        Assert.Equal("87", timelineCardRoot.Attribute("MinHeight")?.Value);
        Assert.Equal("110", timelineCardRoot.Attribute("Width")?.Value);
        Assert.Equal("88", timelineThumbnailVisual.Attribute("Width")?.Value);
        Assert.Equal("54", timelineThumbnailVisual.Attribute("Height")?.Value);
        Assert.Equal("44,27,0", timelineThumbnailVisual.Attribute("CenterPoint")?.Value);
        Assert.Equal("80", timelineImageFrame.Attribute("Width")?.Value);
        Assert.Equal("46", timelineImageFrame.Attribute("Height")?.Value);
        Assert.True(
            double.Parse(timelineCardRoot.Attribute("Width")!.Value, CultureInfo.InvariantCulture)
            - (double.Parse(timelineThumbnailVisual.Attribute("Width")!.Value, CultureInfo.InvariantCulture) * 1.2d)
            >= 4d);
        Assert.True(
            double.Parse(timelineThumbnailSlot.Attribute("Height")!.Value, CultureInfo.InvariantCulture)
            - (double.Parse(timelineThumbnailVisual.Attribute("Height")!.Value, CultureInfo.InvariantCulture) * 1.2d)
            >= 4d);
        Assert.Contains(timelineThumbnailVisual.Descendants(), element =>
            element.Name.LocalName == "Vector3Transition"
            && element.Attribute("Duration")?.Value == "0:0:0.2");
        Assert.Equal("{ThemeResource ScreenshotSelectionFillBrush}", timelineSelectionGlow.Attribute("Background")?.Value);
        Assert.Equal("{ThemeResource ScreenshotSelectionBorderBrush}", timelineSelectionGlow.Attribute("BorderBrush")?.Value);
        Assert.Equal("{ThemeResource ScreenshotSelectionBorderBrush}", timelineSelectionChrome.Attribute("BorderBrush")?.Value);
        Assert.All(new[] { timelineSelectionGlow, timelineSelectionChrome }, element =>
            Assert.Contains(element.Descendants(), child =>
                child.Name.LocalName == "ScalarTransition"
                && child.Attribute("Duration")?.Value == "0:0:0.16"));
        Assert.Equal("10", timelineClockIcon.Attribute("FontSize")?.Value);
        Assert.Equal("{Binding InstallationBrush}", timelineInstallationBadge.Attribute("Background")?.Value);
        Assert.Equal("{Binding InstallationGlyph}", timelineInstallationIcon.Attribute("Glyph")?.Value);
        Assert.Equal("Raw", timelineInstallationIcon.Attribute("AutomationProperties.AccessibilityView")?.Value);
        Assert.Contains("InstallationAppearance.CreateAccentBrush(installation.Color)", timelineSource, StringComparison.Ordinal);
        Assert.Contains("InstallationAppearance.GetIconGlyph(installation.Icon)", timelineSource, StringComparison.Ordinal);
        Assert.Equal("{ThemeResource TextFillColorSecondaryBrush}", timelineClockIcon.Attribute("Foreground")?.Value);
        Assert.Equal("Raw", timelineClockIcon.Attribute("AutomationProperties.AccessibilityView")?.Value);
        Assert.Equal("11", timelineTimeText.Attribute("FontSize")?.Value);
        Assert.Equal("Normal", timelineTimeText.Attribute("FontWeight")?.Value);
        Assert.Equal("{ThemeResource TextFillColorSecondaryBrush}", timelineTimeText.Attribute("Foreground")?.Value);
        Assert.DoesNotContain(timeline.Descendants(), element => element.Attribute("Text")?.Value == "{Binding DateText}");
        Assert.Contains(timeline.Descendants(), element => HasName(element, "TimelineContainerRoot"));
        Assert.DoesNotContain(timeline.Descendants(), element => HasName(element, "FilmstripPanel"));
        Assert.Equal("112", filmstripStrip.Attribute("Height")?.Value);
        Assert.Equal("1", filmstripStrip.Attribute("Opacity")?.Value);
        Assert.NotNull(filmstripSurface);
        Assert.Null(gallerySection.Attribute("Grid.RowSpan"));
        Assert.Equal("4", gallerySection.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", gallerySection.Attribute("Grid.Column")?.Value);
        Assert.Contains(window.Descendants(), element =>
            HasName(element, "TimelineSection") && element.Attribute("Margin")?.Value == "0");
        Assert.Contains(window.Descendants(), element => HasName(element, "DayOverviewSection"));
        Assert.Equal("4", markerCanvas.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("4", selectionCanvas.Attribute("Grid.ColumnSpan")?.Value);
        Assert.NotNull(rangeLabelCanvas);
        Assert.Equal("Bottom", activityBaseline.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("12,0,12,6", activityBaseline.Attribute("Margin")?.Value);
        Assert.Equal("Raw", activityBaseline.Attribute("AutomationProperties.AccessibilityView")?.Value);
        Assert.Equal(5, dayOverview.Descendants().Count(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value.StartsWith("Tick", StringComparison.Ordinal) == true));
        Assert.Contains(dayOverview.Descendants(), element => HasName(element, "SelectionRangeIndicator"));
        Assert.Contains("ScreenshotDayTimelineProjection.Create(items, selectedIndex)", dayOverviewSource, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(button, accessibleName);", dayOverviewSource, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(button, accessibleName);", dayOverviewSource, StringComparison.Ordinal);
        Assert.Contains("height - markerHeight - MarkerBaselineInset", dayOverviewSource, StringComparison.Ordinal);
        Assert.Contains("MarkerCanvas.ActualHeight - SelectionRangeIndicator.Height - MarkerBaselineInset", dayOverviewSource, StringComparison.Ordinal);
        Assert.Contains("DayOverviewSection.SelectedIndexChanged += DayOverviewSection_SelectedIndexChanged;", windowSource, StringComparison.Ordinal);
        Assert.Equal("0", detailsPane.Attribute("Margin")?.Value);
        Assert.Equal("1,0,0,0", detailsPane.Attribute("BorderThickness")?.Value);
        Assert.Equal("0", detailsPane.Attribute("CornerRadius")?.Value);
        Assert.Equal("0,0,0", detailsPane.Attribute("Translation")?.Value);
        Assert.DoesNotContain(detailsPane.Descendants(), element => element.Name.LocalName == "ThemeShadow");
        Assert.Contains("public void SetItem(ScreenshotGalleryItem? item", viewerSource, StringComparison.Ordinal);
        Assert.Contains("public event EventHandler? ZoomStateChanged", viewerSource, StringComparison.Ordinal);
        Assert.Contains("public void ZoomOut()", viewerSource, StringComparison.Ordinal);
        Assert.Contains("public void ResetZoom()", viewerSource, StringComparison.Ordinal);
        Assert.Contains("public void ZoomIn()", viewerSource, StringComparison.Ordinal);
        Assert.Contains("private const float MouseWheelDeltaPerNotch = 120f;", viewerSource, StringComparison.Ordinal);
        Assert.Contains("e.GetCurrentPoint(ImageScroller)", viewerSource, StringComparison.Ordinal);
        Assert.Contains("point.Properties.IsHorizontalMouseWheel", viewerSource, StringComparison.Ordinal);
        Assert.Contains("point.Properties.MouseWheelDelta == 0", viewerSource, StringComparison.Ordinal);
        Assert.Contains("SetZoom(target, point.Position, disableAnimation: true);", viewerSource, StringComparison.Ordinal);
        Assert.Contains("var contentAnchorX = (ImageScroller.HorizontalOffset + anchorX) / currentZoom;", viewerSource, StringComparison.Ordinal);
        Assert.Contains("var contentAnchorY = (ImageScroller.VerticalOffset + anchorY) / currentZoom;", viewerSource, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", viewerSource, StringComparison.Ordinal);
        Assert.Contains("Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5d", timelineSource, StringComparison.Ordinal);
        Assert.Contains("ImageScroller.ChangeView", viewerSource, StringComparison.Ordinal);
        Assert.Contains("GetScreenshotImageAsync", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.Contains("bitmap.SetSourceAsync(stream)", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UriSource", viewerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UriSource", timelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UriSource", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", viewerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", timelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageFile", bitmapLoaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageFailed=", viewer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ImageFailed=", timeline.ToString(), StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", viewerSource, StringComparison.Ordinal);
        Assert.Contains("generation == _imageLoadGeneration", viewerSource, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(image.DataContext, registration.Entry)", timelineSource, StringComparison.Ordinal);
        Assert.Contains("ScreenshotViewer.Configure(_application, _lifetimeCancellation.Token);", windowSource, StringComparison.Ordinal);
        Assert.Contains("TimelineSection.Configure(_application, _lifetimeCancellation.Token);", windowSource, StringComparison.Ordinal);

        var viewerAwait = viewerSource.IndexOf("var result = await loader.LoadAsync", StringComparison.Ordinal);
        var viewerGuard = viewerSource.IndexOf("if (!IsCurrentImageLoad", viewerAwait, StringComparison.Ordinal);
        var viewerAssignment = viewerSource.IndexOf("ScreenshotImage.Source = result.Bitmap;", viewerGuard, StringComparison.Ordinal);
        Assert.True(viewerAwait >= 0 && viewerGuard > viewerAwait && viewerAssignment > viewerGuard);
        var timelineAwait = timelineSource.IndexOf("var result = await loader.LoadAsync", StringComparison.Ordinal);
        var timelineGuard = timelineSource.IndexOf("if (!IsCurrentThumbnailLoad", timelineAwait, StringComparison.Ordinal);
        var timelineAssignment = timelineSource.IndexOf("image.Source = result.Succeeded ? result.Bitmap : null;", timelineGuard, StringComparison.Ordinal);
        Assert.True(timelineAwait >= 0 && timelineGuard > timelineAwait && timelineAssignment > timelineGuard);
        var beginViewerLoad = viewerSource.IndexOf("private void BeginImageLoad", StringComparison.Ordinal);
        var cancelViewerLoad = viewerSource.IndexOf("CancelImageLoad();", beginViewerLoad, StringComparison.Ordinal);
        var registerViewerLoad = viewerSource.IndexOf("_imageLoadCancellation = cancellation;", cancelViewerLoad, StringComparison.Ordinal);
        Assert.True(beginViewerLoad >= 0 && cancelViewerLoad > beginViewerLoad && registerViewerLoad > cancelViewerLoad);
        var beginTimelineLoad = timelineSource.IndexOf("private void BeginTimelineImageLoad", StringComparison.Ordinal);
        var cancelTimelineLoad = timelineSource.IndexOf("CancelTimelineImageLoad(image);", beginTimelineLoad, StringComparison.Ordinal);
        var registerTimelineLoad = timelineSource.IndexOf("_thumbnailLoads[image] = registration;", cancelTimelineLoad, StringComparison.Ordinal);
        Assert.True(beginTimelineLoad >= 0 && cancelTimelineLoad > beginTimelineLoad && registerTimelineLoad > cancelTimelineLoad);
        var setTimelineItems = timelineSource.IndexOf("public void SetItems", StringComparison.Ordinal);
        var cancelAllTimelineLoads = timelineSource.IndexOf("CancelAllThumbnailLoads();", setTimelineItems, StringComparison.Ordinal);
        var advanceTimelineGeneration = timelineSource.IndexOf("_thumbnailGeneration++;", cancelAllTimelineLoads, StringComparison.Ordinal);
        var replaceTimelineItems = timelineSource.IndexOf("FilmstripList.ItemsSource = _entries;", advanceTimelineGeneration, StringComparison.Ordinal);
        Assert.True(
            setTimelineItems >= 0
            && cancelAllTimelineLoads > setTimelineItems
            && advanceTimelineGeneration > cancelAllTimelineLoads
            && replaceTimelineItems > advanceTimelineGeneration);
        Assert.Contains("bitmap.PixelWidth", viewerSource, StringComparison.Ordinal);
        Assert.Contains("var containScale = Math.Min", viewerSource, StringComparison.Ordinal);
        Assert.Contains("PointerDeviceType.Mouse", viewerSource, StringComparison.Ordinal);
        Assert.Contains("ImageScroller.CapturePointer(e.Pointer)", viewerSource, StringComparison.Ordinal);
        Assert.Contains("ImageScroller.ReleasePointerCapture(e.Pointer)", viewerSource, StringComparison.Ordinal);
        Assert.Contains("_dragStartHorizontalOffset - (point.Position.X - _dragStartPosition.X)", viewerSource, StringComparison.Ordinal);
        Assert.Contains("ImageScroller.ChangeView(horizontalOffset, verticalOffset, null, disableAnimation: true)", viewerSource, StringComparison.Ordinal);
        Assert.Contains("FindDescendant<ScrollViewer>", timelineSource, StringComparison.Ordinal);
        Assert.Contains("scroller.ExtentWidth", timelineSource, StringComparison.Ordinal);
        Assert.Contains("private const float SelectedTimelineScale = 1.2f;", timelineSource, StringComparison.Ordinal);
        Assert.Contains("private const double EstimatedTimelineContainerWidth = 184d;", timelineSource, StringComparison.Ordinal);
        Assert.Contains("decodePixelWidth: 432", timelineSource, StringComparison.Ordinal);
        Assert.Contains("cardRoot.FindName(\"TimelineThumbnailVisual\")", timelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Screenshots.Timeline.Date", timelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("string DateText", timelineSource, StringComparison.Ordinal);
        Assert.Contains("args.InRecycleQueue", timelineSource, StringComparison.Ordinal);
        Assert.Contains("container.ContentTemplateRoot", timelineSource, StringComparison.Ordinal);
        Assert.Contains("FilmstripList.ItemsPanelRoot", timelineSource, StringComparison.Ordinal);
        Assert.Contains("ScrollIntoView(selectedItem, ScrollIntoViewAlignment.Leading)", timelineSource, StringComparison.Ordinal);
        Assert.Contains("TransformToVisual(scroller)", timelineSource, StringComparison.Ordinal);
        Assert.Contains("scroller.ViewportWidth / 2d", timelineSource, StringComparison.Ordinal);
        Assert.Contains("scroller.ChangeView(targetOffset, null, null, disableAnimation: false)", timelineSource, StringComparison.Ordinal);
        Assert.Contains("generation == _selectionCenterGeneration", timelineSource, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetZIndex(container, isSelected ? 1 : 0);", timelineSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverFlow.SelectedIndexChanged", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToIndex", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FilmstripPanel.Children.Clear", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new BitmapImage", windowSource, StringComparison.Ordinal);
    }

    private static bool HasName(XElement element, string value)
        => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == value);

    private static bool HasKey(XElement element, string value)
        => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == value);

    private static string RepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "WorkTrail.slnx")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new DirectoryNotFoundException("Repository root could not be located from the test output directory.")
            : Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
    }
}
