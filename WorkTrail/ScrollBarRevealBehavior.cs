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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace WorkTrail;

/// <summary>Reveals descendant scrollbars while the pointer is inside their scroll container.</summary>
public static class ScrollBarRevealBehavior
{
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(220);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollBarRevealBehavior),
        new PropertyMetadata(false, IsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void IsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            throw new InvalidOperationException("Scrollbar reveal can only be attached to a ScrollViewer.");
        }

        if ((bool)args.NewValue)
        {
            scrollViewer.Loaded += ScrollViewer_Loaded;
            scrollViewer.PointerEntered += ScrollViewer_PointerEntered;
            scrollViewer.PointerExited += ScrollViewer_PointerExited;
            return;
        }

        scrollViewer.Loaded -= ScrollViewer_Loaded;
        scrollViewer.PointerEntered -= ScrollViewer_PointerEntered;
        scrollViewer.PointerExited -= ScrollViewer_PointerExited;
    }

    private static void ScrollViewer_Loaded(object sender, RoutedEventArgs args) =>
        SetScrollBarOpacity((ScrollViewer)sender, 0d, TimeSpan.Zero);

    private static void ScrollViewer_PointerEntered(object sender, PointerRoutedEventArgs args) =>
        SetScrollBarOpacity((ScrollViewer)sender, 1d, FadeInDuration);

    private static void ScrollViewer_PointerExited(object sender, PointerRoutedEventArgs args) =>
        SetScrollBarOpacity((ScrollViewer)sender, 0d, FadeOutDuration);

    private static void SetScrollBarOpacity(ScrollViewer scrollViewer, double opacity, TimeSpan duration)
    {
        foreach (var scrollBar in DescendantScrollBars(scrollViewer))
        {
            var animation = new DoubleAnimation
            {
                To = opacity,
                Duration = new Duration(duration)
            };
            Storyboard.SetTarget(animation, scrollBar);
            Storyboard.SetTargetProperty(animation, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
    }

    private static IEnumerable<ScrollBar> DescendantScrollBars(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollBar scrollBar)
            {
                yield return scrollBar;
            }

            foreach (var descendant in DescendantScrollBars(child))
            {
                yield return descendant;
            }
        }
    }
}
