// SPDX-License-Identifier: MIT
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using WorkTrail.Services;

namespace WorkTrail.Controls;

/// <summary>Paints launcher artwork with animated saturation while preserving its alpha channel.</summary>
public sealed class LauncherArtworkBrush : XamlCompositionBrushBase
{
    private LoadedImageSurface? _imageSurface;
    private CompositionSurfaceBrush? _surfaceBrush;
    private CompositionEffectBrush? _effectBrush;
    private bool _highlighted;

    /// <summary>Gets or sets the packaged artwork URI before the brush connects.</summary>
    public Uri? Source { get; set; }

    /// <summary>Animates between half and full saturation from the currently displayed value.</summary>
    public void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
        if (_effectBrush is null) return;
        var compositor = _effectBrush.Compositor;
        using var easing = compositor.CreateLinearEasingFunction();
        using var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(300);
        animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        // StartingValue preserves continuity if the pointer reverses direction during a transition.
        animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        animation.InsertKeyFrame(1f, highlighted ? 1f : 0.5f, easing);
        _effectBrush.StartAnimation("Color.Saturation", animation);
    }

    /// <summary>Connects the packaged image to the XAML compositor's saturation effect.</summary>
    protected override void OnConnected()
    {
        if (_effectBrush is not null || Source is null) return;
        try
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            // Decode near the largest displayed size; Windows applies the display's rasterization scale.
            // This avoids minifying the full 1254px artwork directly in the animated effect.
            _imageSurface = LoadedImageSurface.StartLoadFromUri(Source, new Windows.Foundation.Size(192, 192));
            _imageSurface.LoadCompleted += Image_LoadCompleted;
            _surfaceBrush = compositor.CreateSurfaceBrush(_imageSurface);
            _surfaceBrush.Stretch = CompositionStretch.Uniform;
            _surfaceBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
            using var effect = new SaturationEffect
            {
                Name = "Color",
                Saturation = _highlighted ? 1f : 0.5f,
                Source = new CompositionEffectSourceParameter("Artwork")
            };
            using var factory = compositor.CreateEffectFactory(effect, ["Color.Saturation"]);
            _effectBrush = factory.CreateBrush();
            _effectBrush.SetSourceParameter("Artwork", _surfaceBrush);
            CompositionBrush = _effectBrush;
        }
        catch (Exception exception)
        {
            OnDisconnected();
            ApplicationErrorService.Report(exception, "LauncherArtworkEffect");
        }
    }

    /// <summary>Releases image and compositor resources when the artwork leaves the visual tree.</summary>
    protected override void OnDisconnected()
    {
        CompositionBrush = null;
        _effectBrush?.Dispose();
        _effectBrush = null;
        _surfaceBrush?.Dispose();
        _surfaceBrush = null;
        if (_imageSurface is not null)
        {
            _imageSurface.LoadCompleted -= Image_LoadCompleted;
            _imageSurface.Dispose();
            _imageSurface = null;
        }
    }

    private void Image_LoadCompleted(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
    {
        if (args.Status != LoadedImageSourceLoadStatus.Success)
        {
            // A failed packaged image must be reported instead of silently disappearing.
            ApplicationErrorService.Report(
                new InvalidOperationException($"Launcher artwork failed to load: {args.Status}."),
                "LauncherArtworkImage");
        }
    }
}
