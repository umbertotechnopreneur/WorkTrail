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


using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using Windows.Storage.Streams;

namespace WorkTrail.Controls;

/// <summary>Turns application-owned screenshot bytes into WinUI bitmap sources without performing file I/O.</summary>
internal sealed class ScreenshotBitmapSourceLoader(IWorkTrailApplication application)
{
    private const int MaximumConcurrentImageReads = 4;
    private static readonly SemaphoreSlim ImageReadGate = new(MaximumConcurrentImageReads, MaximumConcurrentImageReads);
    private readonly IWorkTrailApplication _application = application ?? throw new ArgumentNullException(nameof(application));

    /// <summary>Loads and decodes one validated screenshot using the shared application boundary.</summary>
    internal async Task<ScreenshotBitmapLoadResult> LoadAsync(
        string screenshotPath,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        if (!Path.IsPathFullyQualified(screenshotPath))
        {
            throw new ArgumentException("The screenshot path must be fully qualified.", nameof(screenshotPath));
        }

        if (decodePixelWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decodePixelWidth));
        }

        await ImageReadGate.WaitAsync(cancellationToken);
        try
        {
            // The application facade owns containment validation and the only filesystem read.
            var result = await _application.GetScreenshotImageAsync(
                new ScreenshotImageRequest(screenshotPath),
                cancellationToken);
            if (!result.Succeeded || result.Value is null)
            {
                return ScreenshotBitmapLoadResult.Failure(result.Code);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(result.Value.Content);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            cancellationToken.ThrowIfCancellationRequested();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return ScreenshotBitmapLoadResult.Success(bitmap);
        }
        finally
        {
            ImageReadGate.Release();
        }
    }
}

/// <summary>Represents one decoded bitmap or a stable application failure code.</summary>
internal sealed record ScreenshotBitmapLoadResult(bool Succeeded, string Code, BitmapImage? Bitmap)
{
    /// <summary>Creates a successful bitmap result.</summary>
    internal static ScreenshotBitmapLoadResult Success(BitmapImage bitmap) =>
        new(true, "screenshot.image.loaded", bitmap);

    /// <summary>Creates a failed bitmap result.</summary>
    internal static ScreenshotBitmapLoadResult Failure(string code) =>
        new(false, code, null);
}
