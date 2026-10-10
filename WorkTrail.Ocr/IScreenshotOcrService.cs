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


namespace WorkTrail.Ocr;

/// <summary>
/// Extracts raw text and geometry from screenshot image files using an on-device OCR engine.
/// </summary>
public interface IScreenshotOcrService
{
    /// <summary>
    /// Gets whether extraction is enabled for this service instance.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Extracts raw OCR data from an image file.
    /// </summary>
    /// <param name="imagePath">The path of the image to read.</param>
    /// <param name="cancellationToken">A token that cancels file decoding or recognition.</param>
    /// <returns>The immutable local OCR result.</returns>
    /// <exception cref="ArgumentException">The enabled service received an invalid path or language tag.</exception>
    /// <exception cref="FileNotFoundException">The enabled service could not find the image.</exception>
    /// <exception cref="ScreenshotOcrLanguageUnavailableException">The requested or user-profile language is unavailable.</exception>
    /// <exception cref="ScreenshotOcrInteropException">Windows could not open, decode, convert, or recognize the image.</exception>
    Task<ScreenshotOcrResult> ExtractAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
