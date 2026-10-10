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


namespace WorkTrail.Presentation;

/// <summary>Prevents asynchronous screenshot loads from replacing a newer inline search selection.</summary>
public sealed class SearchPreviewSelectionState
{
    private string? _screenshotPath;

    /// <summary>Gets the generation that owns the selected screenshot preview.</summary>
    public int Generation { get; private set; }

    /// <summary>Changes the preview selection and reports whether an image request is needed.</summary>
    public bool Select(string? screenshotPath)
    {
        if (screenshotPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        }

        if (string.Equals(_screenshotPath, screenshotPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _screenshotPath = screenshotPath;
        Generation++;
        return true;
    }

    /// <summary>Identifies whether a completion still belongs to the latest selection.</summary>
    public bool IsCurrent(string screenshotPath, int generation) =>
        generation == Generation && string.Equals(_screenshotPath, screenshotPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Invalidates every pending completion when the owning search window closes.</summary>
    public void Invalidate()
    {
        _screenshotPath = null;
        Generation++;
    }
}
