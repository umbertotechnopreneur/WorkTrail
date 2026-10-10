// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;
using WorkTrail.Application;

namespace WorkTrail.Presentation;

/// <summary>Identifies one safe, presentation-neutral Markdown block.</summary>
public enum SafeMarkdownBlockKind
{
    /// <summary>Displays a short section heading.</summary>
    Heading,

    /// <summary>Displays a regular paragraph.</summary>
    Paragraph,

    /// <summary>Displays an unordered-list item.</summary>
    Bullet,

    /// <summary>Displays an ordered-list item.</summary>
    Numbered
}

/// <summary>Contains plain text extracted from one supported Markdown block.</summary>
public sealed record SafeMarkdownBlock(SafeMarkdownBlockKind Kind, string Text, string? Marker = null);

/// <summary>Contains already-formatted values rendered by the passive screenshot details pane.</summary>
public sealed record ScreenshotDetailsViewState(
    string CapturedDate,
    string CapturedTime,
    string Application,
    string WindowTitle,
    string Screen,
    string CaptureKind,
    string Origin,
    string InstallationName,
    string InstallationMachineName,
    string InstallationColor,
    string InstallationIcon,
    string ActivityIndex,
    string ActivityLabels,
    string MouseClicks,
    string CpuUsage,
    string GpuUsage,
    string? AnalysisTime,
    IReadOnlyList<SafeMarkdownBlock> AiDescription,
    string? OcrText,
    HardwareSnapshotViewState Hardware,
    bool IsVip = false,
    string UserNote = "");

/// <summary>Builds safe screenshot-detail and Markdown projections without presentation-framework dependencies.</summary>
public static partial class ScreenshotDetailsProjection
{
    private const int MaximumMarkdownCharacters = 12_000;
    private const int MaximumMarkdownBlocks = 80;

    /// <summary>Formats one screenshot DTO for the current UI culture.</summary>
    /// <param name="item">Presentation-neutral screenshot data.</param>
    /// <param name="culture">Culture used to format dates and numbers.</param>
    /// <param name="localizedCaptureKind">Localized label for the stable capture-kind identifier.</param>
    /// <param name="localizedOrigin">Localized label for the stable capture-origin identifier.</param>
    /// <param name="missingValue">Placeholder rendered for unavailable optional values.</param>
    /// <param name="translate">Resolves hardware labels in the current UI language.</param>
    public static ScreenshotDetailsViewState Create(
        ScreenshotGalleryItem item,
        CultureInfo culture,
        string localizedCaptureKind,
        string localizedOrigin,
        string missingValue,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedCaptureKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(localizedOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(missingValue);

        var localTime = item.CapturedAt.ToLocalTime();
        var labels = item.SpanLabels is { Count: > 0 }
            ? string.Join("  ·  ", item.SpanLabels.Select(label => label.Label))
            : missingValue;
        var correctedOcrText = item.TextSnapshot?.AiRefinement?.CorrectedText;
        var rawOcrText = item.TextSnapshot?.Ocr.RawText;
        var ocrText = !string.IsNullOrWhiteSpace(correctedOcrText)
            ? correctedOcrText
            : string.IsNullOrWhiteSpace(rawOcrText) ? null : rawOcrText;
        return new ScreenshotDetailsViewState(
            localTime.ToString(culture.DateTimeFormat.LongDatePattern, culture),
            localTime.ToString("T", culture),
            string.IsNullOrWhiteSpace(item.ForegroundApplication) ? missingValue : item.ForegroundApplication,
            string.IsNullOrWhiteSpace(item.ForegroundWindowTitle) ? missingValue : item.ForegroundWindowTitle,
            string.IsNullOrWhiteSpace(item.ScreenName) ? localizedCaptureKind : item.ScreenName,
            localizedCaptureKind,
            localizedOrigin,
            string.IsNullOrWhiteSpace(item.Installation?.FriendlyName) ? missingValue : item.Installation.FriendlyName,
            string.IsNullOrWhiteSpace(item.Installation?.MachineName) ? missingValue : item.Installation.MachineName,
            item.Installation?.Color ?? InstallationProfileCatalog.Colors[0],
            item.Installation?.Icon ?? InstallationProfileCatalog.Icons[0],
            item.ActivityIndex is { } index ? index.ToString(culture) : missingValue,
            labels,
            item.MouseClicks is { } clicks ? clicks.ToString("N0", culture) : missingValue,
            item.CpuUsagePercent is { } cpu ? (cpu / 100d).ToString("P0", culture) : missingValue,
            item.GpuUsagePercent is { } gpu ? (gpu / 100d).ToString("P0", culture) : missingValue,
            item.AiAnalyzedAt?.ToLocalTime().ToString("g", culture),
            ParseMarkdown(item.AiDescriptionMarkdown),
            ocrText,
            HardwareSnapshotProjection.Create(item.HardwareSnapshot, culture, translate),
            item.IsVip,
            item.UserNote);
    }

    /// <summary>
    /// Parses a bounded Markdown subset into inert text blocks; URLs and HTML are never made interactive.
    /// </summary>
    public static IReadOnlyList<SafeMarkdownBlock> ParseMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<SafeMarkdownBlock>();
        }

        var bounded = markdown.Length <= MaximumMarkdownCharacters
            ? markdown
            : string.Concat(markdown.AsSpan(0, MaximumMarkdownCharacters), "…");
        var result = new List<SafeMarkdownBlock>();
        var paragraph = new List<string>();

        foreach (var rawLine in bounded.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushParagraph(result, paragraph);
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                FlushParagraph(result, paragraph);
                AddBlock(result, new SafeMarkdownBlock(SafeMarkdownBlockKind.Heading, CleanInline(heading.Groups[1].Value)));
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                FlushParagraph(result, paragraph);
                AddBlock(result, new SafeMarkdownBlock(SafeMarkdownBlockKind.Bullet, CleanInline(bullet.Groups[1].Value), "•"));
                continue;
            }

            var numbered = NumberedPattern().Match(line);
            if (numbered.Success)
            {
                FlushParagraph(result, paragraph);
                AddBlock(result, new SafeMarkdownBlock(
                    SafeMarkdownBlockKind.Numbered,
                    CleanInline(numbered.Groups[2].Value),
                    numbered.Groups[1].Value + "."));
                continue;
            }

            paragraph.Add(CleanInline(line));
        }

        FlushParagraph(result, paragraph);
        return result;
    }

    /// <summary>Flattens generated Markdown into one bounded inert preview without formatting syntax or targets.</summary>
    public static string ToPlainTextPreview(string? markdown, int maximumCharacters = 120)
    {
        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        var blocks = ParseMarkdown(markdown);
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var substantiveBlocks = blocks.Where(block => block.Kind != SafeMarkdownBlockKind.Heading).ToArray();
        var previewBlocks = substantiveBlocks.Length > 0 ? substantiveBlocks : blocks;
        var preview = string.Join(' ', previewBlocks.Select(block => block.Text)).Trim();
        return preview.Length <= maximumCharacters
            ? preview
            : string.Concat(preview.AsSpan(0, maximumCharacters).TrimEnd(), "…");
    }

    private static void FlushParagraph(List<SafeMarkdownBlock> result, List<string> paragraph)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        AddBlock(result, new SafeMarkdownBlock(SafeMarkdownBlockKind.Paragraph, string.Join(' ', paragraph)));
        paragraph.Clear();
    }

    private static void AddBlock(List<SafeMarkdownBlock> result, SafeMarkdownBlock block)
    {
        if (result.Count >= MaximumMarkdownBlocks || string.IsNullOrWhiteSpace(block.Text))
        {
            return;
        }

        result.Add(block);
    }

    private static string CleanInline(string value)
    {
        // Generated Markdown is rendered as inert text: link targets and HTML tags are discarded by design.
        var text = MarkdownImageOrLinkPattern().Replace(value, match => match.Groups[1].Value);
        text = HtmlTagPattern().Replace(text, string.Empty);
        return text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex(@"^#{1,3}\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^[-+*]\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^(\d+)[.)]\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedPattern();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageOrLinkPattern();

    [GeneratedRegex(@"</?[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagPattern();
}
