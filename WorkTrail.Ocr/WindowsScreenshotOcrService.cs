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
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WorkTrail.Ocr;

/// <summary>
/// Extracts screenshot text on-device through <see cref="OcrEngine"/>.
/// </summary>
/// <remarks>
/// Windows supports <see cref="OcrEngine"/> for desktop applications with package identity.
/// An unpackaged or otherwise unsupported runtime fails explicitly with
/// <see cref="ScreenshotOcrInteropException"/>.
/// </remarks>
public sealed class WindowsScreenshotOcrService : IScreenshotOcrService
{
    private const uint RecognitionDimensionBudget = 2560;

    /// <summary>
    /// Identifies the engine recorded in extraction results.
    /// </summary>
    public const string EngineName = "Windows.Media.Ocr";

    private readonly OcrOptions _options;
    private readonly SemaphoreSlim _extractionGate = new(1, 1);
    private OcrEngine? _engine;

    /// <summary>
    /// Initializes a Windows screenshot OCR service with an immutable option snapshot.
    /// </summary>
    /// <param name="options">The local OCR configuration.</param>
    public WindowsScreenshotOcrService(OcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc />
    public async Task<ScreenshotOcrResult> ExtractAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            // Disabled OCR is a deliberate no-I/O path: do not validate or inspect the image.
            return ScreenshotOcrResult.CreateDisabled(DateTimeOffset.UtcNow, EngineName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? preferredLanguageTag = ValidatePreferredLanguageTag(_options.PreferredLanguageTag);
        string fullImagePath = ValidateImagePath(imagePath);

        await _extractionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Reuse one agile WinRT engine, but serialize decode/recognition to bound peak bitmap memory.
            return await ExtractSerializedAsync(fullImagePath, preferredLanguageTag, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _extractionGate.Release();
        }
    }

    private async Task<ScreenshotOcrResult> ExtractSerializedAsync(
        string fullImagePath,
        string? preferredLanguageTag,
        CancellationToken cancellationToken)
    {
        OcrEngine engine = _engine ??= CreateEngine(preferredLanguageTag);

        using IRandomAccessStream stream = await OpenImageAsync(fullImagePath, cancellationToken)
            .ConfigureAwait(false);
        BitmapDecoder decoder = await CreateDecoderAsync(stream, fullImagePath, cancellationToken)
            .ConfigureAwait(false);

        uint pixelWidth = decoder.PixelWidth;
        uint pixelHeight = decoder.PixelHeight;
        if (pixelWidth == 0 || pixelHeight == 0)
        {
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.DecodeImage,
                $"Image '{fullImagePath}' has invalid zero dimensions.",
                new InvalidDataException("Decoded image dimensions must be positive."));
        }

        uint maximumDimension = Math.Min(OcrEngine.MaxImageDimension, RecognitionDimensionBudget);
        (uint recognitionWidth, uint recognitionHeight) = CalculateRecognitionDimensions(
            pixelWidth,
            pixelHeight,
            maximumDimension);

        using SoftwareBitmap bitmap = await DecodeCompatibleBitmapAsync(
                decoder,
                recognitionWidth,
                recognitionHeight,
                fullImagePath,
                cancellationToken)
            .ConfigureAwait(false);
        Windows.Media.Ocr.OcrResult windowsResult = await RecognizeAsync(
                engine,
                bitmap,
                fullImagePath,
                cancellationToken)
            .ConfigureAwait(false);

        double sourceScaleX = (double)pixelWidth / recognitionWidth;
        double sourceScaleY = (double)pixelHeight / recognitionHeight;
        ImmutableArray<OcrTextLine> lines = ProjectLines(
            windowsResult,
            fullImagePath,
            sourceScaleX,
            sourceScaleY);
        string rawText = windowsResult.Text
            ?? throw CreateProjectionFailure(fullImagePath, "Windows OCR returned a null text value.");
        string effectiveLanguageTag = engine.RecognizerLanguage.LanguageTag;

        return ScreenshotOcrResult.CreateRecognized(
            rawText,
            effectiveLanguageTag,
            windowsResult.TextAngle,
            DateTimeOffset.UtcNow,
            EngineName,
            pixelWidth,
            pixelHeight,
            lines);
    }

    internal static (uint Width, uint Height) CalculateRecognitionDimensions(
        uint sourceWidth,
        uint sourceHeight,
        uint maximumDimension)
    {
        if (sourceWidth == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source width must be positive.");
        }

        if (sourceHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight), "Source height must be positive.");
        }

        if (maximumDimension == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDimension), "Maximum dimension must be positive.");
        }

        if (sourceWidth <= maximumDimension && sourceHeight <= maximumDimension)
        {
            return (sourceWidth, sourceHeight);
        }

        double scale = Math.Min(
            (double)maximumDimension / sourceWidth,
            (double)maximumDimension / sourceHeight);
        uint width = Math.Max(1u, (uint)Math.Floor(sourceWidth * scale));
        uint height = Math.Max(1u, (uint)Math.Floor(sourceHeight * scale));
        return (Math.Min(width, maximumDimension), Math.Min(height, maximumDimension));
    }

    internal static OcrTextRectangle ProjectRectangleToSource(
        double x,
        double y,
        double width,
        double height,
        double sourceScaleX,
        double sourceScaleY)
    {
        if (!double.IsFinite(sourceScaleX) || sourceScaleX <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceScaleX), "Source scale must be finite and positive.");
        }

        if (!double.IsFinite(sourceScaleY) || sourceScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceScaleY), "Source scale must be finite and positive.");
        }

        return new OcrTextRectangle(
            x * sourceScaleX,
            y * sourceScaleY,
            width * sourceScaleX,
            height * sourceScaleY);
    }

    private static string? ValidatePreferredLanguageTag(string? preferredLanguageTag)
    {
        if (preferredLanguageTag is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(preferredLanguageTag) ||
            !string.Equals(preferredLanguageTag, preferredLanguageTag.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "PreferredLanguageTag must be null or a non-blank BCP-47 tag without surrounding whitespace.",
                nameof(OcrOptions.PreferredLanguageTag));
        }

        return preferredLanguageTag;
    }

    private static string ValidateImagePath(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        string fullImagePath = Path.GetFullPath(imagePath);

        if (!File.Exists(fullImagePath))
        {
            throw new FileNotFoundException("The OCR source image does not exist.", fullImagePath);
        }

        return fullImagePath;
    }

    private static OcrEngine CreateEngine(string? preferredLanguageTag)
    {
        try
        {
            var availableLanguages = OcrEngine.AvailableRecognizerLanguages
                .Select(static language => language.LanguageTag)
                .ToImmutableArray();

            if (preferredLanguageTag is null)
            {
                return OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new ScreenshotOcrLanguageUnavailableException(null, availableLanguages);
            }

            Language requestedLanguage;
            try
            {
                requestedLanguage = new Language(preferredLanguageTag);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    $"'{preferredLanguageTag}' is not a valid Windows BCP-47 language tag.",
                    nameof(OcrOptions.PreferredLanguageTag),
                    exception);
            }

            if (!OcrEngine.IsLanguageSupported(requestedLanguage))
            {
                throw new ScreenshotOcrLanguageUnavailableException(
                    preferredLanguageTag,
                    availableLanguages);
            }

            return OcrEngine.TryCreateFromLanguage(requestedLanguage)
                ?? throw new ScreenshotOcrLanguageUnavailableException(
                    preferredLanguageTag,
                    availableLanguages);
        }
        catch (ScreenshotOcrLanguageUnavailableException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Engine activation has no fallback because a different recognizer would change semantics.
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.InitializeEngine,
                "Windows.Media.Ocr could not initialize an on-device recognizer.",
                exception);
        }
    }

    private static async Task<IRandomAccessStream> OpenImageAsync(
        string fullImagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // StorageFile is required by the WinRT decoder; failures remain visible to the caller.
            StorageFile file = await StorageFile.GetFileFromPathAsync(fullImagePath)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            return await file.OpenAsync(FileAccessMode.Read)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.OpenImage,
                $"Windows could not open OCR source image '{fullImagePath}'.",
                exception);
        }
    }

    private static async Task<BitmapDecoder> CreateDecoderAsync(
        IRandomAccessStream stream,
        string fullImagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await BitmapDecoder.CreateAsync(stream)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.DecodeImage,
                $"Windows could not decode OCR source image '{fullImagePath}'.",
                exception);
        }
    }

    private static async Task<SoftwareBitmap> DecodeCompatibleBitmapAsync(
        BitmapDecoder decoder,
        uint recognitionWidth,
        uint recognitionHeight,
        string fullImagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Request the same BGRA8 premultiplied SoftwareBitmap format used by the Windows OCR sample.
            SoftwareBitmap bitmap;
            if (decoder.PixelWidth == recognitionWidth && decoder.PixelHeight == recognitionHeight)
            {
                bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var transform = new BitmapTransform
                {
                    ScaledWidth = recognitionWidth,
                    ScaledHeight = recognitionHeight,
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };
                bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied ||
                bitmap.PixelWidth != checked((int)recognitionWidth) ||
                bitmap.PixelHeight != checked((int)recognitionHeight))
            {
                bitmap.Dispose();
                throw new InvalidDataException("The decoder did not produce the requested OCR bitmap format and dimensions.");
            }

            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.ConvertImage,
                $"Windows could not convert OCR source image '{fullImagePath}' to BGRA8.",
                exception);
        }
    }

    private static async Task<Windows.Media.Ocr.OcrResult> RecognizeAsync(
        OcrEngine engine,
        SoftwareBitmap bitmap,
        string fullImagePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Recognition stays on-device; cancellation is forwarded to the WinRT async operation.
            return await engine.RecognizeAsync(bitmap)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ScreenshotOcrInteropException(
                OcrInteropStage.RecognizeText,
                $"Windows OCR failed while recognizing image '{fullImagePath}'.",
                exception);
        }
    }

    private static ImmutableArray<OcrTextLine> ProjectLines(
        Windows.Media.Ocr.OcrResult windowsResult,
        string fullImagePath,
        double sourceScaleX,
        double sourceScaleY)
    {
        try
        {
            var lines = ImmutableArray.CreateBuilder<OcrTextLine>(windowsResult.Lines.Count);
            foreach (Windows.Media.Ocr.OcrLine line in windowsResult.Lines)
            {
                var words = ImmutableArray.CreateBuilder<OcrTextWord>(line.Words.Count);
                foreach (Windows.Media.Ocr.OcrWord word in line.Words)
                {
                    Windows.Foundation.Rect rectangle = word.BoundingRect;
                    string wordText = word.Text
                        ?? throw new InvalidDataException("Windows OCR returned a null word value.");
                    words.Add(new OcrTextWord(
                        wordText,
                        ProjectRectangleToSource(
                            rectangle.X,
                            rectangle.Y,
                            rectangle.Width,
                            rectangle.Height,
                            sourceScaleX,
                            sourceScaleY)));
                }

                string lineText = line.Text
                    ?? throw new InvalidDataException("Windows OCR returned a null line value.");
                lines.Add(new OcrTextLine(lineText, words.MoveToImmutable()));
            }

            return lines.MoveToImmutable();
        }
        catch (Exception exception)
        {
            throw CreateProjectionFailure(fullImagePath, "Windows OCR returned an invalid raw result.", exception);
        }
    }

    private static ScreenshotOcrInteropException CreateProjectionFailure(
        string fullImagePath,
        string message,
        Exception? innerException = null)
    {
        return new ScreenshotOcrInteropException(
            OcrInteropStage.RecognizeText,
            $"{message} Source image: '{fullImagePath}'.",
            innerException ?? new InvalidDataException(message));
    }
}
