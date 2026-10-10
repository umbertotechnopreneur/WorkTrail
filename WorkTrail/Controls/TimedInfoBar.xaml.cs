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


using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace WorkTrail.Controls;

/// <summary>Renders one compact, closable toast with a severity icon.</summary>
public sealed partial class TimedInfoBar : UserControl
{
    private const float BannerElevation = 18f;
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(80);

    private Storyboard? _activeTransition;
    private long _transitionGeneration;
    private bool _allowCloseCommit;
    private bool _isDismissing;
    private bool _isPresented;

    /// <summary>Creates an initially collapsed timed banner host.</summary>
    public TimedInfoBar()
    {
        InitializeComponent();
        ElementCompositionPreview.SetIsTranslationEnabled(BannerSurface, true);
        BannerSurface.Translation = new Vector3(0f, 0f, BannerElevation);
        Unloaded += TimedInfoBar_Unloaded;
    }

    /// <summary>Occurs when the banner is closed manually, programmatically, or because its host unloads.</summary>
    internal event EventHandler? Dismissed;

    /// <summary>Displays a banner without owning its timeout lifecycle.</summary>
    /// <param name="title">The notification heading.</param>
    /// <param name="message">Optional supporting text.</param>
    /// <param name="severity">The notification's icon and color category.</param>
    internal void Present(string title, string message, InfoBarSeverity severity)
    {
        _transitionGeneration++;
        StopActiveTransition();
        _isDismissing = false;
        BannerInfoBar.Title = title;
        BannerInfoBar.Message = message;
        BannerInfoBar.Severity = severity;
        // Informational messages share the green palette while retaining their native information icon.
        var state = severity switch
        {
            InfoBarSeverity.Warning => "Warning",
            InfoBarSeverity.Error => "Error",
            _ => "Success"
        };
        VisualStateManager.GoToState(this, state, useTransitions: false);
        var animate = AnimationsAreEnabled();
        BannerSurface.Opacity = 1d;
        Visibility = Visibility.Visible;
        _isPresented = true;
        BannerInfoBar.IsOpen = true;
        if (animate)
        {
            StartOpacityTransition(1d, completion: null, initialOpacity: 0d);
        }
    }

    /// <summary>Closes the current banner with its existing fade transition.</summary>
    internal void Dismiss()
    {
        if (!_isPresented || _isDismissing)
        {
            return;
        }

        _isDismissing = true;
        var generation = ++_transitionGeneration;
        if (AnimationsAreEnabled())
        {
            StartOpacityTransition(0d, () => CompleteDismissal(generation));
            return;
        }

        // A close button raises Closing synchronously. Queue the commit so a cancelled close is never re-entered.
        if (!DispatcherQueue.TryEnqueue(() => CompleteDismissal(generation)))
        {
            CompleteDismissal(generation);
        }
    }

    private void BannerInfoBar_Closing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (_allowCloseCommit)
        {
            return;
        }

        args.Cancel = true;
        Dismiss();
    }

    /// <summary>Stops pending transitions and closes synchronously without scheduling work on a shutting-down window.</summary>
    internal void DismissImmediately()
    {
        // Invalidate queued commits and animation completions before stopping their storyboard.
        var generation = ++_transitionGeneration;
        StopActiveTransition();
        _isDismissing = true;
        CompleteDismissal(generation);
    }

    private void TimedInfoBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_isPresented)
        {
            return;
        }

        DismissImmediately();
    }

    private void CompleteDismissal(long generation)
    {
        if (!_isPresented || !_isDismissing || generation != _transitionGeneration)
        {
            return;
        }

        StopActiveTransition();
        _isPresented = false;
        _isDismissing = false;
        BannerSurface.Opacity = 0d;
        _allowCloseCommit = true;
        try
        {
            BannerInfoBar.IsOpen = false;
        }
        finally
        {
            _allowCloseCommit = false;
        }

        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void StartOpacityTransition(double targetOpacity, Action? completion, double? initialOpacity = null)
    {
        StopActiveTransition();
        var generation = _transitionGeneration;
        var animation = new DoubleAnimation
        {
            From = initialOpacity ?? BannerSurface.Opacity,
            To = targetOpacity,
            Duration = new Duration(FadeDuration),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, BannerSurface);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (generation != _transitionGeneration)
            {
                return;
            }

            BannerSurface.Opacity = targetOpacity;
            _activeTransition = null;
            completion?.Invoke();
        };
        _activeTransition = storyboard;
        storyboard.Begin();
    }

    private void StopActiveTransition()
    {
        _activeTransition?.Stop();
        _activeTransition = null;
    }

    private static bool AnimationsAreEnabled() => new UISettings().AnimationsEnabled;
}
