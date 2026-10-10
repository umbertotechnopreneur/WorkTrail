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


using Microsoft.UI.Xaml.Controls;

namespace WorkTrail;

/// <summary>Renders localized reset warnings over theme-aware Acrylic and decorative artwork.</summary>
public sealed partial class AtomicResetDialog : ContentDialog
{
    /// <summary>Initializes one confirmation step without performing any reset operation.</summary>
    internal AtomicResetDialog(DialogRequest request)
    {
        InitializeComponent();
        Title = request.Title;
        WarningMessage.Text = request.Message;
        PrimaryButtonText = request.PrimaryButtonText;
        CloseButtonText = request.CloseButtonText;
    }
}
