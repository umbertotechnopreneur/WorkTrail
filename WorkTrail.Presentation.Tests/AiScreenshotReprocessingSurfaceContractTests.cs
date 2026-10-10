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

public sealed class AiScreenshotReprocessingSurfaceContractTests
{
    [Fact]
    public void Dialog_IsAcrylicAndMakesScreenshotCaptureAndRequestCountsExplicit()
    {
        var document = XDocument.Load(RepositoryFile("WorkTrail", "AiScreenshotReprocessingDialogWindow.xaml"));
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "AiScreenshotReprocessingDialogWindow.xaml.cs"));

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "DesktopAcrylicBackdrop");
        Assert.Equal("Transparent", Named(document, "RootGrid").Attribute("Background")?.Value);
        Assert.True(int.Parse(Named(document, "MissingScreenshotsValueText").Attribute("FontSize")!.Value) >= 32);
        Assert.True(int.Parse(Named(document, "MissingCapturesValueText").Attribute("FontSize")!.Value) >= 32);
        Assert.True(int.Parse(Named(document, "MaximumRequestsValueText").Attribute("FontSize")!.Value) >= 32);
        Assert.Equal("AiReprocess.Screenshots", TaggedLabel(document, "MissingScreenshotsValueText"));
        Assert.Equal("AiReprocess.Captures", TaggedLabel(document, "MissingCapturesValueText"));
        Assert.Equal("AiReprocess.Requests", TaggedLabel(document, "MaximumRequestsValueText"));
        Assert.Equal("PauseResumeButton_Click", Named(document, "PauseResumeButton").Attribute("Click")?.Value);
        Assert.Equal("StartButton_Click", Named(document, "StartButton").Attribute("Click")?.Value);
        Assert.Contains(document.Descendants(), element => HasName(element, "JobProgressBar"));
        Assert.Contains(document.Descendants(), element => HasName(element, "CompletedScreenshotsText"));
        Assert.Contains(document.Descendants(), element => HasName(element, "RemainingScreenshotsText"));
        Assert.Contains(document.Descendants(), element => HasName(element, "CloseKeepsRunningText"));

        Assert.Contains("_application.PreviewAiScreenshotReprocessingAsync", source, StringComparison.Ordinal);
        Assert.Contains("_application.StartAiScreenshotReprocessingAsync", source, StringComparison.Ordinal);
        Assert.Contains("_application.GetAiScreenshotReprocessingJobAsync", source, StringComparison.Ordinal);
        Assert.Contains("_application.PauseAiScreenshotReprocessingAsync", source, StringComparison.Ordinal);
        Assert.Contains("_application.ResumeAiScreenshotReprocessingAsync", source, StringComparison.Ordinal);
        Assert.Contains("plan.ProcessableTodayScreenshotCount", source, StringComparison.Ordinal);
        Assert.Contains("result.Value.ActiveJobId", source, StringComparison.Ordinal);
        Assert.Contains("PollInterval = TimeSpan.FromMilliseconds(500)", source, StringComparison.Ordinal);
        Assert.Contains("WindowStateKeys.AiScreenshotReprocessing", source, StringComparison.Ordinal);
        Assert.Contains("Polling is presentation-owned", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
    }

    private static string? TaggedLabel(XDocument document, string valueName)
    {
        var value = Named(document, valueName);
        var stack = value.Parent ?? throw new InvalidDataException($"{valueName} has no label container.");
        return stack.Elements().Single(element => element != value && element.Name.LocalName == "TextBlock")
            .Attribute("Tag")?.Value;
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => HasName(element, name));

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
