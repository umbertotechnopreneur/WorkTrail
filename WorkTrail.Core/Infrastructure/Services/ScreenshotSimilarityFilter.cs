// SPDX-License-Identifier: MIT
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkTrail.Services;

internal sealed record ScreenshotSimilarityOptions(
    bool Enabled = true,
    int SampleWidth = 128,
    int SampleHeight = 96,
    double MaxMeanDifference = 0.25,
    double MaxChangedPixelFraction = 0.0001,
    int PixelDifferenceThreshold = 6,
    int MaxPixelDifference = 24,
    int ForceRefreshMinutes = 15,
    int MaxEntries = 128)
{
    internal void Validate()
    {
        if (SampleWidth is < 16 or > 512 || SampleHeight is < 16 or > 512
            || !double.IsFinite(MaxMeanDifference) || MaxMeanDifference is < 0 or > 255
            || !double.IsFinite(MaxChangedPixelFraction) || MaxChangedPixelFraction is < 0 or > 1
            || PixelDifferenceThreshold is < 0 or > 255 || MaxPixelDifference is < 0 or > 255
            || ForceRefreshMinutes is < 1 or > 120 || MaxEntries is < 1 or > 512)
        {
            throw new ArgumentException("Screenshot similarity options are outside their supported range.");
        }
    }
}

internal sealed class ScreenshotVisualFingerprint
{
    private readonly byte[] _pixels;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly int _sampleWidth;
    private readonly int _sampleHeight;

    private ScreenshotVisualFingerprint(byte[] pixels, int sourceWidth, int sourceHeight,
        int sampleWidth, int sampleHeight)
    {
        _pixels = pixels;
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _sampleWidth = sampleWidth;
        _sampleHeight = sampleHeight;
    }

    internal static ScreenshotVisualFingerprint Create(Bitmap bitmap, ScreenshotSimilarityOptions options)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        using var thumbnail = new Bitmap(options.SampleWidth, options.SampleHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(thumbnail))
        using (var attributes = new ImageAttributes())
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(bitmap, new Rectangle(0, 0, thumbnail.Width, thumbnail.Height),
                0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
        }

        var pixels = new byte[checked(thumbnail.Width * thumbnail.Height * 3)];
        var bitmapData = thumbnail.LockBits(new Rectangle(0, 0, thumbnail.Width, thumbnail.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            // Copy only RGB data, not stride padding; no screenshot or thumbnail is written to disk.
            int rowLength = thumbnail.Width * 3;
            for (int row = 0; row < thumbnail.Height; row++)
            {
                Marshal.Copy(IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride),
                    pixels, row * rowLength, rowLength);
            }
        }
        finally
        {
            thumbnail.UnlockBits(bitmapData);
        }

        return new ScreenshotVisualFingerprint(pixels, bitmap.Width, bitmap.Height,
            thumbnail.Width, thumbnail.Height);
    }

    internal bool IsSimilarTo(ScreenshotVisualFingerprint other, ScreenshotSimilarityOptions options)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (_sourceWidth != other._sourceWidth || _sourceHeight != other._sourceHeight
            || _sampleWidth != other._sampleWidth || _sampleHeight != other._sampleHeight
            || _sampleWidth != options.SampleWidth || _sampleHeight != options.SampleHeight)
        {
            return false;
        }

        long totalDifference = 0;
        int changedPixels = 0;
        double allowedChangedPixels = (_pixels.Length / 3) * options.MaxChangedPixelFraction;
        for (int index = 0; index < _pixels.Length; index += 3)
        {
            int blue = Math.Abs(_pixels[index] - other._pixels[index]);
            int green = Math.Abs(_pixels[index + 1] - other._pixels[index + 1]);
            int red = Math.Abs(_pixels[index + 2] - other._pixels[index + 2]);
            int largestDifference = Math.Max(blue, Math.Max(green, red));
            totalDifference += blue + green + red;

            // A small but strong local change must not disappear into the whole-screen average.
            if (largestDifference > options.MaxPixelDifference)
            {
                return false;
            }

            if (largestDifference > options.PixelDifferenceThreshold && ++changedPixels > allowedChangedPixels)
            {
                return false;
            }
        }

        return (double)totalDifference / _pixels.Length <= options.MaxMeanDifference;
    }
}

internal sealed class ScreenshotSimilarityFilter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RetainedFingerprint> _retained = new(StringComparer.Ordinal);

    internal bool IsUnchanged(string key, ScreenshotVisualFingerprint candidate,
        DateTimeOffset now, ScreenshotSimilarityOptions options, Func<string, bool> retainedFileExists) =>
        FindMatch(key, candidate, now, options, retainedFileExists) is not null;

    internal string? FindMatch(string key, ScreenshotVisualFingerprint candidate,
        DateTimeOffset now, ScreenshotSimilarityOptions options, Func<string, bool> retainedFileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(retainedFileExists);
        options.Validate();
        if (!options.Enabled)
        {
            return null;
        }

        lock (_gate)
        {
            TrimToCapacity(options.MaxEntries);
            if (!_retained.TryGetValue(key, out var retained)
                || now < retained.RetainedAt
                || now - retained.RetainedAt >= TimeSpan.FromMinutes(options.ForceRefreshMinutes))
            {
                return null;
            }

            if (!retainedFileExists(retained.Path))
            {
                _retained.Remove(key);
                return null;
            }

            // Reading or rejecting a candidate never advances the baseline or its refresh deadline.
            return candidate.IsSimilarTo(retained.Fingerprint, options) ? retained.Path : null;
        }
    }

    internal void Remember(string key, ScreenshotVisualFingerprint fingerprint,
        DateTimeOffset retainedAt, ScreenshotSimilarityOptions options, string retainedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedPath);
        options.Validate();
        if (!options.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_retained.TryGetValue(key, out var existing) && retainedAt < existing.RetainedAt)
            {
                return;
            }

            // The capture owner calls this only after successfully publishing the retained image.
            _retained[key] = new RetainedFingerprint(fingerprint, retainedAt, retainedPath);
            TrimToCapacity(options.MaxEntries);
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _retained.Clear();
        }
    }

    private void TrimToCapacity(int capacity)
    {
        while (_retained.Count > capacity)
        {
            string oldestKey = _retained.OrderBy(pair => pair.Value.RetainedAt)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
            _retained.Remove(oldestKey);
        }
    }

    private sealed record RetainedFingerprint(ScreenshotVisualFingerprint Fingerprint, DateTimeOffset RetainedAt, string Path);
}
