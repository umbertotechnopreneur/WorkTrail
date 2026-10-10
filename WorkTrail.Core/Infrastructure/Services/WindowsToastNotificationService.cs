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
using Windows.UI.Notifications;

namespace WorkTrail.Services;

/// <summary>Shows short-lived Windows toast notifications without coupling the application layer to WinUI.</summary>
public interface IWindowsToastNotificationService
{
    /// <summary>Attempts to show one Windows toast notification.</summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    /// <returns><see langword="true"/> when Windows accepted the toast.</returns>
    bool TryShow(string title, string message);
}

/// <summary>Provides the Windows toast adapter used by the desktop presentation host.</summary>
public sealed class WindowsToastNotificationService : IWindowsToastNotificationService
{
    private readonly ILogger<WindowsToastNotificationService> _logger;

    /// <summary>Gets or sets whether automatic Windows notifications may be displayed.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Creates the Windows toast adapter.</summary>
    /// <param name="logger">Logger used when the optional toast channel is unavailable.</param>
    public WindowsToastNotificationService(ILogger<WindowsToastNotificationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool TryShow(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsEnabled) return false;

        try
        {
            var content = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = content.GetElementsByTagName("text");
            textNodes.Item(0).AppendChild(content.CreateTextNode(Trim(title, 160)));
            textNodes.Item(1).AppendChild(content.CreateTextNode(Trim(message, 1_500)));
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(content));
            return true;
        }
        catch (Exception exception)
        {
            // Toasts are optional OS integration; the caller keeps its in-app notification fallback when Windows rejects this channel.
            _logger.LogWarning(exception, "Windows toast notification could not be shown. ExceptionType={ExceptionType}", exception.GetType().Name);
            return false;
        }
    }

    private static string Trim(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)]}…";
}
