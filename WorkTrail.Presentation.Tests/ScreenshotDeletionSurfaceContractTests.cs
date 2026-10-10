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
using Xunit;

namespace WorkTrail.Presentation.Tests;

/// <summary>Guards standard confirmation, reentrancy, and refresh behavior for screenshot deletion commands.</summary>
public sealed class ScreenshotDeletionSurfaceContractTests
{
    /// <summary>Ensures both destructive paths use the owned standard dialog and one guarded operation.</summary>
    [Fact]
    public void ScreenshotDeletionUsesOwnedStandardConfirmationAndRefreshesTheGallery()
    {
        var source = File.ReadAllText(RepositoryFile("WorkTrail", "ScreenshotWindow.xaml.cs"));

        Assert.Contains("_dialogs.ConfirmAsync(", source, StringComparison.Ordinal);
        Assert.Contains("DialogRequest.Confirmation", source, StringComparison.Ordinal);
        Assert.Contains("Screenshots.DeleteScreenshot.Confirm.Title", source, StringComparison.Ordinal);
        Assert.Contains("Screenshots.DeleteAnalysis.Confirm.Title", source, StringComparison.Ordinal);
        Assert.Contains("_deleteOperationInProgress", source, StringComparison.Ordinal);
        Assert.Contains("_application.DeleteScreenshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("_application.DeleteScreenshotAnalysisAsync", source, StringComparison.Ordinal);
        var deleteOperation = MethodBody(
            source,
            "private async Task RunConfirmedDeletionAsync(",
            "private async void HeaderSection_SaveRequested");
        Assert.Contains("HeaderSection.SetDeletionActionsEnabled(false);", deleteOperation, StringComparison.Ordinal);
        Assert.Contains("await LoadGalleryAsync(_selectedDate);", deleteOperation, StringComparison.Ordinal);
        Assert.Contains("HeaderSection.SetDeletionActionsEnabled(true);", deleteOperation, StringComparison.Ordinal);
        Assert.Contains("HeaderSection.SetAnalysisDeletionAvailable(", source, StringComparison.Ordinal);
        Assert.Contains("CloseOcrTextWindowForScreenshot(selected.Path);", deleteOperation, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string startSignature, string nextSignature)
    {
        var start = source.IndexOf(startSignature, StringComparison.Ordinal);
        var end = source.IndexOf(nextSignature, start + startSignature.Length, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidDataException($"Could not isolate method '{startSignature}'.");
        }

        return source[start..end];
    }

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
