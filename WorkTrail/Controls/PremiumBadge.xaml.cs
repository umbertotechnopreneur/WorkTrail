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


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace WorkTrail.Controls;

/// <summary>Renders the same localized, high-contrast-aware Premium marker on headers and commands.</summary>
public sealed partial class PremiumBadge : UserControl
{
    /// <summary>Creates a non-interactive entitlement marker.</summary>
    public PremiumBadge() => InitializeComponent();

    /// <summary>Sets the localized caption and the identical accessible name.</summary>
    public string Text
    {
        get => Caption.Text;
        set { Caption.Text = value; AutomationProperties.SetName(this, value); }
    }
}
