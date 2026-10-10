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


namespace WorkTrail.Services;

/// <summary>Validates WorkTrail screenshot ownership before invoking the shared Windows file-share service.</summary>
public sealed class ScreenshotShareService
{
    private readonly WindowsFileShareService _fileShare;

    /// <summary>Creates the screenshot-specific share adapter.</summary>
    public ScreenshotShareService(WindowsFileShareService? fileShare = null) =>
        _fileShare = fileShare ?? new WindowsFileShareService();

    /// <summary>
    /// Registers the selected screenshot as shareable content and opens the Windows Share UI.
    /// </summary>
    /// <param name="screenshotPath">Absolute path to a WorkTrail-owned screenshot artifact.</param>
    /// <param name="windowHandle">HWND that owns the Share UI.</param>
    /// <returns>The validated screenshot path supplied to the Share UI.</returns>
    public string Share(string screenshotPath, IntPtr windowHandle)
    {
        var fullPath = ValidateScreenshotPath(screenshotPath);
        return _fileShare.Share(fullPath, windowHandle, Path.GetFileName(fullPath), "WorkTrail screenshot");
    }

    private static string ValidateScreenshotPath(string screenshotPath)
    {
        if (string.IsNullOrWhiteSpace(screenshotPath) || !Path.IsPathFullyQualified(screenshotPath))
        {
            throw new ArgumentException("The screenshot path must be an absolute path.", nameof(screenshotPath));
        }

        var fullPath = Path.GetFullPath(screenshotPath);
        if (!ScreenCaptureService.IsOwnedArtifact(fullPath))
        {
            throw new ArgumentException("The path is not a WorkTrail-owned screenshot artifact.", nameof(screenshotPath));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The WorkTrail screenshot no longer exists.", fullPath);
        }

        return fullPath;
    }
}
