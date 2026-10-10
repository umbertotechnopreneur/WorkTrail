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


using System.Collections.Immutable;

namespace WorkTrail.Ocr;

/// <summary>
/// Describes the outcome of a local OCR extraction.
/// </summary>
public enum OcrExtractionStatus
{
    /// <summary>
    /// OCR was disabled and the image was not accessed.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// OCR completed and returned non-whitespace text.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// OCR completed but returned no non-whitespace text.
    /// </summary>
    NoText = 2,
}

/// <summary>
/// Represents an OCR rectangle in source-image pixels.
/// </summary>
public readonly record struct OcrTextRectangle
{
    /// <summary>
    /// Initializes a source-image rectangle.
    /// </summary>
    /// <param name="x">The horizontal coordinate of the top-left corner.</param>
    /// <param name="y">The vertical coordinate of the top-left corner.</param>
    /// <param name="width">The non-negative rectangle width.</param>
    /// <param name="height">The non-negative rectangle height.</param>
    public OcrTextRectangle(double x, double y, double width, double height)
    {
        EnsureFinite(x, nameof(x));
        EnsureFinite(y, nameof(y));
        EnsureFinite(width, nameof(width));
        EnsureFinite(height, nameof(height));

        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Rectangle width cannot be negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Rectangle height cannot be negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the horizontal coordinate of the top-left corner.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the vertical coordinate of the top-left corner.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Gets the rectangle width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the rectangle height.
    /// </summary>
    public double Height { get; }

    private static void EnsureFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Rectangle values must be finite.");
        }
    }
}

/// <summary>
/// Represents one raw word returned by Windows OCR.
/// </summary>
public sealed record OcrTextWord
{
    /// <summary>
    /// Initializes a recognized word.
    /// </summary>
    /// <param name="text">The unmodified word text.</param>
    /// <param name="boundingRectangle">The word bounds in source-image pixels.</param>
    public OcrTextWord(string text, OcrTextRectangle boundingRectangle)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        BoundingRectangle = boundingRectangle;
    }

    /// <summary>
    /// Gets the unmodified recognized word text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the word bounds in source-image pixels.
    /// </summary>
    public OcrTextRectangle BoundingRectangle { get; }
}

/// <summary>
/// Represents one raw text line returned by Windows OCR.
/// </summary>
public sealed record OcrTextLine
{
    /// <summary>
    /// Initializes a recognized line and takes an immutable copy of its words.
    /// </summary>
    /// <param name="text">The unmodified line text.</param>
    /// <param name="words">The words detected in the line.</param>
    public OcrTextLine(string text, IEnumerable<OcrTextWord> words)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);

        var builder = ImmutableArray.CreateBuilder<OcrTextWord>();
        foreach (OcrTextWord? word in words)
        {
            if (word is null)
            {
                throw new ArgumentException("OCR lines cannot contain null words.", nameof(words));
            }

            builder.Add(word);
        }

        Text = text;
        Words = builder.ToImmutable();
    }

    /// <summary>
    /// Gets the unmodified recognized line text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the immutable words detected in the line.
    /// </summary>
    public ImmutableArray<OcrTextWord> Words { get; }
}

/// <summary>
/// Contains the immutable raw output of a screenshot OCR operation.
/// </summary>
public sealed record ScreenshotOcrResult
{
    private ScreenshotOcrResult(
        OcrExtractionStatus status,
        string rawText,
        string? effectiveLanguageTag,
        double? textAngleDegrees,
        DateTimeOffset completedAtUtc,
        string engineName,
        uint? pixelWidth,
        uint? pixelHeight,
        ImmutableArray<OcrTextLine> lines)
    {
        Status = status;
        RawText = rawText;
        EffectiveLanguageTag = effectiveLanguageTag;
        TextAngleDegrees = textAngleDegrees;
        CompletedAtUtc = completedAtUtc;
        EngineName = engineName;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Lines = lines;
    }

    /// <summary>
    /// Gets the extraction outcome.
    /// </summary>
    public OcrExtractionStatus Status { get; }

    /// <summary>
    /// Gets the complete, unmodified text returned by the OCR engine.
    /// </summary>
    public string RawText { get; }

    /// <summary>
    /// Gets the actual BCP-47 language tag selected by the OCR engine, or <see langword="null"/> when disabled.
    /// </summary>
    public string? EffectiveLanguageTag { get; }

    /// <summary>
    /// Gets the clockwise text rotation in degrees, or <see langword="null"/> when Windows could not determine it.
    /// </summary>
    public double? TextAngleDegrees { get; }

    /// <summary>
    /// Gets the UTC timestamp recorded after extraction completed or was skipped.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the OCR engine identifier.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the decoded source-image width, or <see langword="null"/> when disabled.
    /// </summary>
    public uint? PixelWidth { get; }

    /// <summary>
    /// Gets the decoded source-image height, or <see langword="null"/> when disabled.
    /// </summary>
    public uint? PixelHeight { get; }

    /// <summary>
    /// Gets the immutable raw line and word structure returned by the OCR engine.
    /// </summary>
    public ImmutableArray<OcrTextLine> Lines { get; }

    internal static ScreenshotOcrResult CreateDisabled(DateTimeOffset completedAtUtc, string engineName)
    {
        EnsureUtc(completedAtUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineName);

        return new ScreenshotOcrResult(
            OcrExtractionStatus.Disabled,
            string.Empty,
            null,
            null,
            completedAtUtc,
            engineName,
            null,
            null,
            []);
    }

    internal static ScreenshotOcrResult CreateRecognized(
        string rawText,
        string effectiveLanguageTag,
        double? textAngleDegrees,
        DateTimeOffset completedAtUtc,
        string engineName,
        uint pixelWidth,
        uint pixelHeight,
        ImmutableArray<OcrTextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveLanguageTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineName);
        EnsureUtc(completedAtUtc);

        if (textAngleDegrees is double angle && !double.IsFinite(angle))
        {
            throw new ArgumentOutOfRangeException(nameof(textAngleDegrees), angle, "Text angle must be finite.");
        }

        if (pixelWidth == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Image width must be positive.");
        }

        if (pixelHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Image height must be positive.");
        }

        if (lines.IsDefault)
        {
            throw new ArgumentException("OCR lines must be initialized.", nameof(lines));
        }

        OcrExtractionStatus status = string.IsNullOrWhiteSpace(rawText)
            ? OcrExtractionStatus.NoText
            : OcrExtractionStatus.Succeeded;

        return new ScreenshotOcrResult(
            status,
            rawText,
            effectiveLanguageTag,
            textAngleDegrees,
            completedAtUtc,
            engineName,
            pixelWidth,
            pixelHeight,
            lines);
    }

    private static void EnsureUtc(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("OCR timestamps must use a zero UTC offset.", nameof(timestamp));
        }
    }
}
