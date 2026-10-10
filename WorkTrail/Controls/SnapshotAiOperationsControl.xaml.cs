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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Collects screen-capture and AI-analysis input and renders facade results.</summary>
public sealed partial class SnapshotAiOperationsControl : UserControl
{
    private LocalizationService _strings = new("system");
    private OperationsSectionContext? _context;
    private bool _hasScreenshotResult;
    private string? _latestScreenshotPath;
    private AiAnalysis? _analysis;

    /// <summary>Creates the independent screen-capture and AI operations surface.</summary>
    public SnapshotAiOperationsControl() => InitializeComponent();

    /// <summary>Applies an explicit language override or resolves the Windows UI language for system mode.</summary>
    public void ApplyLanguage(string language)
    {
        _strings = new LocalizationService(language);
        UiLocalization.Apply(this, _strings);
        if (_hasScreenshotResult)
        {
            RenderLatestScreenshot(_latestScreenshotPath);
        }
        if (_analysis is { } analysis)
        {
            RenderAnalysis(analysis);
        }
    }

    /// <summary>Connects the passive surface to the application facade owned by the composition root.</summary>
    internal void Initialize(IWorkTrailApplication application, MicaDialogService dialogs, Window ownerWindow, TimedInfoBar banner) =>
        _context = new OperationsSectionContext(
            application,
            dialogs,
            ownerWindow,
            banner,
            active =>
            {
                Progress.IsActive = active;
                Progress.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            },
            SectionBody,
            key => _strings.TryTranslate(key, out var value) ? value : null);

    private OperationsSectionContext Context => _context ?? throw new InvalidOperationException("SnapshotAiOperationsControl must be initialized before use.");

    private async void LatestScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await Context.ExecuteAsync((application, token) => application.GetLatestScreenshotAsync(token));
        if (result is { Succeeded: true })
        {
            RenderLatestScreenshot(result.Value);
        }
    }

    private async void OpenScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await Context.ExecuteAsync((application, token) => application.OpenScreenshotFolderAsync(token));
    }

    private void RenderLatestScreenshot(string? screenshotPath)
    {
        _hasScreenshotResult = true;
        _latestScreenshotPath = screenshotPath;
        if (string.IsNullOrWhiteSpace(screenshotPath))
        {
            ScreenshotResultText.Text = _strings.Translate("Operations.Snapshot.None");
            ToolTipService.SetToolTip(ScreenshotResultText, null);
            AutomationProperties.SetHelpText(ScreenshotResultText, string.Empty);
            return;
        }

        ScreenshotResultText.Text = FileNameFromPath(screenshotPath);
        ToolTipService.SetToolTip(ScreenshotResultText, screenshotPath);
        AutomationProperties.SetHelpText(ScreenshotResultText, screenshotPath);
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var request = new AnalyzeCurrentActivityRequest(AllowAiCaptureBox.IsChecked == true, "winui.operations");
        var result = await Context.ExecuteAsync((application, token) => application.AnalyzeCurrentActivityAsync(request, token));
        if (result is { Succeeded: true, Value: { } analysis })
        {
            RenderAnalysis(analysis);
        }
    }

    private void RenderAnalysis(AiAnalysis analysis)
    {
        _analysis = analysis;
        AiAnalysisText.Text = $"{analysis.Application} · {analysis.Context}\n{analysis.Summary}";
    }

    private static string FileNameFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmedPath = path.TrimEnd('\\', '/');
        var separatorIndex = Math.Max(trimmedPath.LastIndexOf('\\'), trimmedPath.LastIndexOf('/'));
        return separatorIndex >= 0 ? trimmedPath[(separatorIndex + 1)..] : trimmedPath;
    }
}
