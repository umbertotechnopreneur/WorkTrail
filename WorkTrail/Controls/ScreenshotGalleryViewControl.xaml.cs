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

namespace WorkTrail.Controls;

/// <summary>Displays the clean selected screenshot surface and its empty/loading states.</summary>
public sealed partial class ScreenshotGalleryViewControl : UserControl
{
    /// <summary>Creates the gallery view control.</summary>
    public ScreenshotGalleryViewControl() => InitializeComponent();

    /// <summary>Gets the gallery surface that hosts pointer interactions.</summary>
    public Grid Surface => GallerySurface;

    /// <summary>Gets the single-image zoomable screenshot viewer.</summary>
    public ScreenshotImageViewerControl Viewer => ImageViewer;

    /// <summary>Gets the empty-state panel.</summary>
    public Grid EmptyPanel => EmptyGalleryPanel;

    /// <summary>Gets the empty-state message text element.</summary>
    public TextBlock EmptyText => EmptyGalleryText;

    /// <summary>Gets the loading indicator.</summary>
    public ProgressRing LoadingRing => GalleryProgressRing;

    /// <summary>Gets the reusable VIP strip displayed above the selected image.</summary>
    public ScreenshotTimelineControl FeaturedTimeline => VipTimeline;

    /// <summary>Gets the header and timeline for VIP captures in the current date.</summary>
    public StackPanel FeaturedSection => VipSection;
}
