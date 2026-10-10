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


using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace WorkTrail.Controls;

/// <summary>Renders a clipped, continuously flowing accent without moving the input or doing per-frame UI work.</summary>
public sealed class SearchActivityLine : Grid
{
    private readonly UISettings _uiSettings = new();
    private SpriteVisual? _line;
    private CompositionLinearGradientBrush? _brush;
    private bool _isSearching;
    private bool _motionEnabled = true;
    private bool _isAnimating;

    /// <summary>Creates a presentation-only accent that respects the Windows animation preference.</summary>
    public SearchActivityLine()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    internal void SetSearching(bool isSearching)
    {
        _isSearching = isSearching;
        if (_line is not null)
        {
            _line.Opacity = isSearching ? 1f : 0.55f;
        }
    }

    internal void SetMotionEnabled(bool enabled)
    {
        _motionEnabled = enabled;
        UpdateMotion();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _brush = compositor.CreateLinearGradientBrush();
        _brush.MappingMode = CompositionMappingMode.Relative;
        _brush.StartPoint = new Vector2(-1f, 0f);
        _brush.EndPoint = new Vector2(1f, 0f);
        var colors = new[]
        {
            ColorHelper.FromArgb(255, 61, 166, 169),
            ColorHelper.FromArgb(255, 102, 133, 215),
            ColorHelper.FromArgb(255, 152, 112, 198)
        };
        // Two identical color cycles make the loop seamless; only the brush coordinates move.
        for (var index = 0; index <= 6; index++)
        {
            _brush.ColorStops.Add(compositor.CreateColorGradientStop(index / 6f, colors[index % colors.Length]));
        }

        _line = compositor.CreateSpriteVisual();
        _line.Brush = _brush;
        _line.Clip = compositor.CreateInsetClip();
        _line.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        ElementCompositionPreview.SetElementChildVisual(this, _line);
        SetSearching(_isSearching);
        UpdateMotion();
    }

    private void UpdateMotion()
    {
        if (_brush is null)
        {
            return; // An unloaded accent has no compositor resources to animate.
        }

        // Activation rechecks the current Windows preference, including after returning from Settings.
        var shouldAnimate = _motionEnabled && _uiSettings.AnimationsEnabled;
        if (shouldAnimate == _isAnimating)
        {
            return;
        }

        _isAnimating = shouldAnimate;
        if (!shouldAnimate)
        {
            // Reduced motion keeps a static accent and the separate textual search status.
            _brush.StopAnimation(nameof(CompositionLinearGradientBrush.StartPoint));
            _brush.StopAnimation(nameof(CompositionLinearGradientBrush.EndPoint));
            return;
        }

        var compositor = _brush.Compositor;
        using var easing = compositor.CreateLinearEasingFunction();
        using var start = compositor.CreateVector2KeyFrameAnimation();
        using var end = compositor.CreateVector2KeyFrameAnimation();
        start.InsertKeyFrame(0f, new Vector2(-1f, 0f));
        start.InsertKeyFrame(1f, new Vector2(0f, 0f), easing);
        end.InsertKeyFrame(0f, new Vector2(1f, 0f));
        end.InsertKeyFrame(1f, new Vector2(2f, 0f), easing);
        start.Duration = end.Duration = TimeSpan.FromSeconds(3.2);
        start.IterationBehavior = end.IterationBehavior = AnimationIterationBehavior.Forever;
        start.Target = nameof(CompositionLinearGradientBrush.StartPoint);
        end.Target = nameof(CompositionLinearGradientBrush.EndPoint);
        using var group = compositor.CreateAnimationGroup();
        group.Add(start);
        group.Add(end);
        _brush.StartAnimationGroup(group);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_line is not null)
        {
            _line.Size = new Vector2((float)args.NewSize.Width, (float)args.NewSize.Height);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _brush?.StopAnimation(nameof(CompositionLinearGradientBrush.StartPoint));
        _brush?.StopAnimation(nameof(CompositionLinearGradientBrush.EndPoint));
        ElementCompositionPreview.SetElementChildVisual(this, null);
        _line?.Clip?.Dispose();
        _line?.Dispose();
        _brush?.Dispose();
        _line = null;
        _brush = null;
        _isAnimating = false;
    }
}
