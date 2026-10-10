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
using Microsoft.UI.Xaml.Markup;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Renders catalog metadata and gates passive content using a runtime-supplied access snapshot.</summary>
[ContentProperty(Name = nameof(Body))]
public sealed partial class FeatureGate : UserControl
{
    /// <summary>Identifies the catalog feature.</summary>
    public static readonly DependencyProperty FeatureProperty = DependencyProperty.Register(nameof(Feature), typeof(ProductFeature), typeof(FeatureGate), new PropertyMetadata(ProductFeature.Tracking, OnPresentationChanged));
    /// <summary>Identifies the passive feature controls.</summary>
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(UIElement), typeof(FeatureGate), new PropertyMetadata(null, OnPresentationChanged));
    /// <summary>Identifies the runtime entitlement snapshot.</summary>
    public static readonly DependencyProperty AccessProperty = DependencyProperty.Register(nameof(Access), typeof(FeatureAccessSnapshot), typeof(FeatureGate), new PropertyMetadata(null, OnPresentationChanged));
    /// <summary>Identifies the current UI language.</summary>
    public static readonly DependencyProperty UiLanguageProperty = DependencyProperty.Register(nameof(UiLanguage), typeof(string), typeof(FeatureGate), new PropertyMetadata("system", OnPresentationChanged));
    /// <summary>Controls title visibility on compact surfaces.</summary>
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(nameof(ShowTitle), typeof(bool), typeof(FeatureGate), new PropertyMetadata(true, OnPresentationChanged));
    /// <summary>Controls inline hint visibility on compact surfaces.</summary>
    public static readonly DependencyProperty ShowHintProperty = DependencyProperty.Register(nameof(ShowHint), typeof(bool), typeof(FeatureGate), new PropertyMetadata(true, OnPresentationChanged));

    /// <summary>Creates the reusable feature presentation.</summary>
    public FeatureGate() { InitializeComponent(); Render(); }
    /// <summary>Gets or sets the catalog feature.</summary>
    public ProductFeature Feature { get => (ProductFeature)GetValue(FeatureProperty); set => SetValue(FeatureProperty, value); }
    /// <summary>Gets or sets the passive feature controls.</summary>
    public UIElement? Body { get => (UIElement?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    /// <summary>Gets or sets access supplied by the facade, never inferred from settings.</summary>
    public FeatureAccessSnapshot? Access { get => (FeatureAccessSnapshot?)GetValue(AccessProperty); set => SetValue(AccessProperty, value); }
    /// <summary>Gets or sets the UI language.</summary>
    public string UiLanguage { get => (string)GetValue(UiLanguageProperty); set => SetValue(UiLanguageProperty, value); }
    /// <summary>Gets or sets whether to display the feature title.</summary>
    public bool ShowTitle { get => (bool)GetValue(ShowTitleProperty); set => SetValue(ShowTitleProperty, value); }
    /// <summary>Gets or sets whether to display an inline access hint.</summary>
    public bool ShowHint { get => (bool)GetValue(ShowHintProperty); set => SetValue(ShowHintProperty, value); }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((FeatureGate)sender).Render();

    private void Render()
    {
        if (BodyHost is null) return;
        var definition = FeatureCatalog.Get(Feature);
        var strings = new LocalizationService(UiLanguage);
        var premium = definition.RequiredTier == ProductTier.Premium;
        var allowed = Access is { } access && FeatureCatalog.IsAllowed(Feature, access);
        var hint = strings.Translate(Access is null ? "Premium.Loading" : "Premium.Required");
        FeatureTitle.Text = strings.Translate(definition.TitleKey);
        FeatureTitle.Visibility = ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = strings.Translate("Premium.Badge");
        PremiumBadge.Visibility = premium ? Visibility.Visible : Visibility.Collapsed;
        var compact = !ShowTitle && !ShowHint;
        GateLayout.ColumnSpacing = compact && premium ? 8 : 0;
        GateLayout.RowSpacing = compact ? 0 : 6;
        Grid.SetColumnSpan(FeatureHeading, compact ? 1 : 2);
        Grid.SetRow(BodyHost, compact ? 0 : 2);
        Grid.SetColumn(BodyHost, compact ? 1 : 0);
        Grid.SetColumnSpan(BodyHost, compact ? 1 : 2);
        AutomationProperties.SetName(PremiumBadge, BadgeText.Text);
        AccessHint.Text = hint;
        AccessHint.Visibility = !allowed && ShowHint ? Visibility.Visible : Visibility.Collapsed;
        BodyHost.Content = Body;
        BodyHost.IsEnabled = allowed;
        ToolTipService.SetToolTip(this, allowed ? FeatureTitle.Text : hint);
        AutomationProperties.SetName(this, premium ? $"{FeatureTitle.Text} · {BadgeText.Text}" : FeatureTitle.Text);
        AutomationProperties.SetHelpText(this, allowed ? "" : hint);
    }
}
