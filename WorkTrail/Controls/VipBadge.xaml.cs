// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace WorkTrail.Controls;

/// <summary>Renders the shared gold crown, with a text label independent of its color.</summary>
public sealed partial class VipBadge : UserControl
{
    /// <summary>Identifies the localized accessible label.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(VipBadge), new PropertyMetadata("VIP", PresentationChanged));
    /// <summary>Identifies whether to show the caption beside the crown.</summary>
    public static readonly DependencyProperty ShowTextProperty = DependencyProperty.Register(
        nameof(ShowText), typeof(bool), typeof(VipBadge), new PropertyMetadata(true, PresentationChanged));
    /// <summary>Creates the non-interactive VIP marker.</summary>
    public VipBadge() { InitializeComponent(); Render(); }
    /// <summary>Gets or sets the localized accessible label.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    /// <summary>Gets or sets whether to show the caption.</summary>
    public bool ShowText { get => (bool)GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }

    // sender identifies the VIP marker whose presentation changed.
    // args contains the dependency-property update.
    private static void PresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((VipBadge)sender).Render();

    private void Render()
    {
        if (Caption is null) return;
        Caption.Text = Text;
        Caption.Visibility = ShowText ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, Text);
        ToolTipService.SetToolTip(this, Text);
    }
}
