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

/// <summary>Guards the passive, lazy, bounded-memory presentation optimizations.</summary>
public sealed class PerformanceOptimizationSurfaceContractTests
{
    [Fact]
    public void MainPreview_UsesBoundedDecodeAndReleasesInvalidSource()
    {
        var view = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var previewImage = view.Descendants().Single(element => HasName(element, "LastScreenshotImage"));

        Assert.Contains("private const int ScreenshotPreviewDecodePixelWidth = 384;", source, StringComparison.Ordinal);
        Assert.Contains("_screenshotBitmapLoader.LoadAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ScreenshotPreviewDecodePixelWidth,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UriSource", source, StringComparison.Ordinal);
        Assert.Contains("LastScreenshotImage.Source = null;", source, StringComparison.Ordinal);
        Assert.Contains("_latestScreenshotCapturedAt == capturedAt", source, StringComparison.Ordinal);
        Assert.Null(previewImage.Attribute("ImageFailed"));

        var loadAwait = source.IndexOf("var result = await _screenshotBitmapLoader.LoadAsync", StringComparison.Ordinal);
        var currentGuard = source.IndexOf("if (!IsCurrentLatestScreenshotLoad", loadAwait, StringComparison.Ordinal);
        var sourceAssignment = source.IndexOf("LastScreenshotImage.Source = result.Bitmap;", currentGuard, StringComparison.Ordinal);
        Assert.True(loadAwait >= 0 && currentGuard > loadAwait && sourceAssignment > currentGuard);
        var beginLoad = source.IndexOf("private void BeginLatestScreenshotLoad", StringComparison.Ordinal);
        var cancelLoad = source.IndexOf("CancelLatestScreenshotLoad();", beginLoad, StringComparison.Ordinal);
        var registerLoad = source.IndexOf("_latestScreenshotLoadCancellation = cancellation;", cancelLoad, StringComparison.Ordinal);
        Assert.True(beginLoad >= 0 && cancelLoad > beginLoad && registerLoad > cancelLoad);
        Assert.Contains("generation == _latestScreenshotLoadGeneration", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_latestScreenshotLoadCancellation, cancellation)", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_latestScreenshotPath, screenshotPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HeavyPagesAndOperationDetails_AreConstructedOnlyOnDemand()
    {
        var main = XDocument.Load(RepositoryFile("WorkTrail", "MainWindow.xaml"));
        var mainSource = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));
        var operations = XDocument.Load(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml"));
        var operationsSource = File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OperationsControl.xaml.cs"));

        Assert.Contains(main.Descendants(), element => element.Name.LocalName == "ContentPresenter" && HasName(element, "OptionsHost"));
        Assert.Contains(main.Descendants(), element => element.Name.LocalName == "ContentPresenter" && HasName(element, "OperationsHost"));
        Assert.DoesNotContain(main.Descendants(), element => element.Name.LocalName is "OptionsControl" or "OperationsControl");
        Assert.Contains("private Task EnsureOptionsAsync()", mainSource, StringComparison.Ordinal);
        Assert.Contains("private Task EnsureOperationsAsync()", mainSource, StringComparison.Ordinal);
        Assert.Contains("options.InitializeAsync(_application, AiState, _lifecycle.Token)", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public async void Initialize", File.ReadAllText(RepositoryFile("WorkTrail", "Controls", "OptionsControl.xaml.cs")), StringComparison.Ordinal);

        string[] detailHosts = ["SnapshotAiHost", "PrivacyHost", "RetentionHost", "PluginsHost", "InstallationTransferHost"];
        Assert.All(detailHosts, name => Assert.Contains(operations.Descendants(), element => element.Name.LocalName == "ContentPresenter" && HasName(element, name)));
        Assert.DoesNotContain(
            operations.Descendants(),
            element => element.Name.LocalName.EndsWith("OperationsControl", StringComparison.Ordinal));
        Assert.Contains("private FrameworkElement EnsureSection", operationsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MainDashboardSubscription_FollowsWindowVisibility()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "MainWindow.xaml.cs"));

        Assert.Contains("&& _appWindow.IsVisible", source, StringComparison.Ordinal);
        Assert.Contains("if (args.DidVisibilityChange)", source, StringComparison.Ordinal);
        Assert.Contains("_dashboardSubscription.Dispose();", source, StringComparison.Ordinal);
    }

    private static bool HasName(XElement element, string name) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name);

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
}
