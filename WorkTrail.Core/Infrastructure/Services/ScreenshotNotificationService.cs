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


using Microsoft.Extensions.Logging;
using SkiaSharp;
using WorkTrail.Application;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;

namespace WorkTrail.Services;

/// <summary>Publishes capture notices independently of any visible application window.</summary>
public interface IScreenshotNotificationService : IDisposable
{
    /// <summary>Shows a saved capture using the current notification preference.</summary>
    /// <param name="screenshotPath">The privacy-approved image produced by the capture pipeline.</param>
    void Show(string screenshotPath);

    /// <summary>Removes capture notices and their temporary images.</summary>
    void Clear();
}

/// <summary>Defines the only protocol actions accepted from screenshot notifications.</summary>
public static class ScreenshotNotificationActivation
{
    /// <summary>Opens the existing WorkTrail interface.</summary>
    public const string Open = "worktrail://notifications/screenshots/open";

    /// <summary>Disables capture notifications without changing capture or error reporting.</summary>
    public const string Disable = "worktrail://notifications/screenshots/disable";

    /// <summary>Rejects protocol input outside the two fixed notification actions.</summary>
    /// <param name="uri">The URI delivered by Windows activation.</param>
    public static bool IsSupported(string? uri) => uri is Open or Disable;
}

/// <summary>Shows Windows capture notifications with bounded, short-lived PNG thumbnails.</summary>
public sealed class ScreenshotNotificationService : IScreenshotNotificationService
{
    private const string Group = "screenshots";
    private const int MaximumThumbnails = 32;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly SettingsSnapshot _settings;
    private readonly ILogger<ScreenshotNotificationService> _logger;
    private readonly Timer _cleanup;
    private string? _directory;
    private bool _disposed;

    /// <summary>Creates the runtime-owned screenshot notification adapter.</summary>
    /// <param name="settings">The authoritative preferences shared by capture and settings commands.</param>
    /// <param name="logger">Reports optional Windows integration failures without image contents.</param>
    public ScreenshotNotificationService(SettingsSnapshot settings, ILogger<ScreenshotNotificationService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cleanup = new Timer(_ => CleanupExpiredThumbnails(), null, Lifetime, TimeSpan.FromMinutes(1));
        // A previous abrupt stop may have left previews behind; they are never part of the screenshot archive.
        Clear();
    }

    /// <inheritdoc />
    public void Show(string screenshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        lock (_gate)
        {
            if (_disposed || !_settings.Value.NotificationsEnabled || !_settings.Value.ScreenshotNotificationsEnabled) return;
            string? thumbnail = null;
            try
            {
                // Keep Windows-compatible previews separate from the original WebP archive.
                _directory ??= Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "screenshot-notifications");
                Directory.CreateDirectory(_directory);
                CleanupThumbnails(removeAll: false);
                if (Directory.EnumerateFiles(_directory, "*.png").Take(MaximumThumbnails).Count() >= MaximumThumbnails)
                    throw new IOException("Screenshot thumbnail cache is full.");
                thumbnail = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".png");
                WriteThumbnail(screenshotPath, thumbnail);
                var settings = _settings.Value;
                if (!settings.NotificationsEnabled || !settings.ScreenshotNotificationsEnabled)
                {
                    File.Delete(thumbnail);
                    return;
                }
                var strings = new LocalizationService(settings.UiLanguage);
                var imageUri = "ms-appdata:///temp/screenshot-notifications/" + Path.GetFileName(thumbnail);
                var content = CreateContent(strings, imageUri);
                var toast = new ToastNotification(content)
                {
                    Group = Group,
                    Tag = Path.GetFileNameWithoutExtension(thumbnail)[..16],
                    ExpirationTime = DateTimeOffset.UtcNow.Add(Lifetime)
                };
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch (Exception exception)
            {
                // An optional OS notice must never turn a successful capture into a failed one.
                if (thumbnail is not null) TryDeleteThumbnail(thumbnail);
                _logger.LogWarning("Screenshot notification could not be shown. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
        }
    }

    /// <summary>Builds a notification without interpolating user text into XML or protocol arguments.</summary>
    /// <param name="strings">The current application language.</param>
    /// <param name="imageUri">The app-owned PNG thumbnail URI.</param>
    /// <exception cref="ArgumentNullException">The localization service is missing.</exception>
    /// <exception cref="ArgumentException">The image URI is outside the generated thumbnail cache.</exception>
    public static XmlDocument CreateContent(LocalizationService strings, string imageUri)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUri);
        const string imagePrefix = "ms-appdata:///temp/screenshot-notifications/";
        if (!imageUri.StartsWith(imagePrefix, StringComparison.Ordinal)
            || !imageUri.EndsWith(".png", StringComparison.Ordinal)
            || !Guid.TryParseExact(imageUri[imagePrefix.Length..^4], "N", out _))
            throw new ArgumentException("Notification image must be an app-owned thumbnail.", nameof(imageUri));
        var content = new XmlDocument();
        content.LoadXml("<toast activationType=\"protocol\"><visual><binding template=\"ToastGeneric\"><text/><text/><image placement=\"hero\"/></binding></visual><actions><action activationType=\"protocol\"/></actions><audio silent=\"true\"/></toast>");
        content.DocumentElement.SetAttribute("launch", ScreenshotNotificationActivation.Open);
        var text = content.GetElementsByTagName("text");
        text.Item(0).AppendChild(content.CreateTextNode(strings.Translate("Notification.ScreenshotCaptured.Title")));
        text.Item(1).AppendChild(content.CreateTextNode(strings.Translate("Notification.ScreenshotCaptured.Message")));
        var image = (XmlElement)content.GetElementsByTagName("image").Item(0);
        image.SetAttribute("src", imageUri);
        image.SetAttribute("alt", strings.Translate("Notification.ScreenshotCaptured.Title"));
        var action = (XmlElement)content.GetElementsByTagName("action").Item(0);
        action.SetAttribute("content", strings.Translate("Notification.ScreenshotCaptured.Disable"));
        action.SetAttribute("arguments", ScreenshotNotificationActivation.Disable);
        return content;
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            try { ToastNotificationManager.History.RemoveGroup(Group); }
            catch (Exception exception)
            {
                _logger.LogWarning("Screenshot notification history could not be cleared. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
            try
            {
                _directory ??= Path.Combine(ApplicationData.Current.TemporaryFolder.Path, "screenshot-notifications");
                CleanupThumbnails(removeAll: true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Screenshot notification cleanup failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _cleanup.Dispose();
            Clear();
        }
    }

    // source is an image owned and approved by the capture pipeline.
    // destination is a unique PNG in the app's temporary thumbnail directory.
    private static void WriteThumbnail(string source, string destination)
    {
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException("Screenshot image cannot be decoded.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 64_000_000)
            throw new InvalidDataException("Screenshot dimensions exceed the thumbnail limit.");
        var size = codec.GetScaledDimensions(Math.Min(1f, 640f / Math.Max(info.Width, info.Height)));
        using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(size.Width, size.Height))
            ?? throw new InvalidDataException("Screenshot thumbnail cannot be decoded.");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("Screenshot thumbnail cannot be encoded.");
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        encoded.SaveTo(output);
    }

    private void CleanupExpiredThumbnails()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { CleanupThumbnails(removeAll: false); }
            catch (Exception exception)
            {
                _logger.LogWarning("Expired screenshot thumbnails could not be removed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
        }
    }

    // removeAll also removes previews when notifications are disabled or the runtime closes.
    private void CleanupThumbnails(bool removeAll)
    {
        if (_directory is null || !Directory.Exists(_directory)) return;
        var cutoff = DateTime.UtcNow.Subtract(Lifetime);
        var files = new DirectoryInfo(_directory).GetFiles("*.png").OrderByDescending(file => file.CreationTimeUtc).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            if (removeAll || files[index].CreationTimeUtc <= cutoff || index >= MaximumThumbnails - 1)
                TryDeleteThumbnail(files[index].FullName);
        }
    }

    // path is a generated thumbnail; capture originals are never deleted here.
    private void TryDeleteThumbnail(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Screenshot thumbnail deletion failed. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
    }
}
