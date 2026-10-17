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


using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Application;
using WorkTrail.Presentation;

namespace WorkTrail.Controls;

/// <summary>Passively renders safe metadata and Markdown blocks for the selected screenshot.</summary>
public sealed partial class ScreenshotDetailsControl : UserControl
{
    /// <summary>Creates the screenshot detail pane.</summary>
    public ScreenshotDetailsControl() => InitializeComponent();

    /// <summary>Replaces every displayed value with one immutable screenshot-detail projection.</summary>
    /// <param name="state">The selected screenshot details, or <see langword="null"/> when no screenshot is selected.</param>
    /// <param name="emptyAiDescriptionText">Localized contextual copy to show when the description has no rendered blocks.</param>
    /// <param name="privacyStatusText">Localized application-wide privacy-filter status.</param>
    public void Render(
        ScreenshotDetailsViewState? state,
        string emptyAiDescriptionText,
        string privacyStatusText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emptyAiDescriptionText);
        ArgumentException.ThrowIfNullOrWhiteSpace(privacyStatusText);
        VipDetailsSection.Visibility = state?.IsVip == true ? Visibility.Visible : Visibility.Collapsed;
        VipNoteText.Text = state?.UserNote ?? string.Empty;
        VipNoteLabel.Visibility = VipNoteText.Visibility = string.IsNullOrWhiteSpace(state?.UserNote) ? Visibility.Collapsed : Visibility.Visible;

        CapturedAtSummaryText.Text = state is null ? "--" : $"{state.CapturedDate} · {state.CapturedTime}";
        ActivityIndexValueText.Text = state?.ActivityIndex ?? "--";
        ApplicationValueText.Text = state?.Application ?? "--";
        WindowTitleValueText.Text = state?.WindowTitle ?? "--";
        ScreenValueText.Text = state?.Screen ?? "--";
        CaptureKindValueText.Text = state?.CaptureKind ?? "--";
        OriginValueText.Text = state?.Origin ?? "--";
        InstallationNameValueText.Text = state?.InstallationName ?? "--";
        InstallationMachineValueText.Text = state?.InstallationMachineName ?? "--";
        InstallationBadge.Background = InstallationAppearance.CreateAccentBrush(
            state?.InstallationColor ?? InstallationProfileCatalog.Colors[0]);
        InstallationIcon.Glyph = InstallationAppearance.GetIconGlyph(
            state?.InstallationIcon ?? InstallationProfileCatalog.Icons[0]);
        AutomationProperties.SetName(
            InstallationBadge,
            state is null ? "--" : $"{state.InstallationName} · {state.InstallationMachineName}");
        ActivityLabelsValueText.Text = state?.ActivityLabels ?? "--";
        MouseClicksValueText.Text = state?.MouseClicks ?? "--";
        PrivacyStatusValueText.Text = privacyStatusText;
        AnalysisTimeValueText.Text = state?.AnalysisTime ?? "--";
        AnalysisTimePanel.Visibility = string.IsNullOrWhiteSpace(state?.AnalysisTime)
            ? Visibility.Collapsed
            : Visibility.Visible;

        AiMarkdownHost.Children.Clear();
        var blocks = state?.AiDescription ?? Array.Empty<SafeMarkdownBlock>();
        NoAiDescriptionText.Text = emptyAiDescriptionText;
        NoAiDescriptionText.Visibility = blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var block in blocks)
        {
            AiMarkdownHost.Children.Add(CreateMarkdownBlock(block));
        }
    }

    private static TextBlock CreateMarkdownBlock(SafeMarkdownBlock block)
    {
        var isHeading = block.Kind == SafeMarkdownBlockKind.Heading;
        var prefix = block.Kind is SafeMarkdownBlockKind.Bullet or SafeMarkdownBlockKind.Numbered
            ? $"{block.Marker}  "
            : string.Empty;
        return new TextBlock
        {
            Margin = block.Kind is SafeMarkdownBlockKind.Bullet or SafeMarkdownBlockKind.Numbered
                ? new Thickness(4, 0, 0, 0)
                : new Thickness(0),
            FontSize = isHeading ? 16 : 14,
            FontWeight = isHeading ? FontWeights.SemiBold : FontWeights.Normal,
            IsTextSelectionEnabled = true,
            LineHeight = isHeading ? 22 : 20,
            Opacity = 1d,
            Text = prefix + block.Text,
            TextWrapping = TextWrapping.Wrap
        };
    }
}
