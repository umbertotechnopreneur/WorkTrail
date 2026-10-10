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
using Microsoft.UI.Xaml.Controls;
using WorkTrail.Presentation;

namespace WorkTrail.Controls;

/// <summary>Passively renders the title and source of one selectable screenshot match.</summary>
public sealed partial class SearchResultItemControl : UserControl
{
    /// <summary>Identifies the immutable result rendered by this control.</summary>
    public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(
        nameof(Result), typeof(ScreenshotSearchResult), typeof(SearchResultItemControl),
        new PropertyMetadata(null, OnResultChanged));

    /// <summary>Creates an empty screenshot-result renderer.</summary>
    public SearchResultItemControl() => InitializeComponent();

    /// <summary>Gets or sets the immutable result rendered by the control.</summary>
    public ScreenshotSearchResult? Result
    {
        get => (ScreenshotSearchResult?)GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    private static void OnResultChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (SearchResultItemControl)dependencyObject;
        var result = (ScreenshotSearchResult?)args.NewValue;
        control.DataContext = result;
        SearchTextHighlight.Apply(control.ResultTitleText, result?.TitleDisplay, result?.Query);
    }
}
