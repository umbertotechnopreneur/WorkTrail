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


using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkTrail.Application;
using WorkTrail.Services;
using Windows.Foundation;

namespace WorkTrail.Controls;

/// <summary>Displays one selected screenshot in a passive zoomable viewer.</summary>
public sealed partial class ScreenshotImageViewerControl : UserControl
{
    private const double ScreenshotFrameMargin = 36d;
    private const float MinimumZoomFactor = 1f;
    private const float MaximumZoomFactor = 5f;
    private const float ZoomStep = 0.25f;
    private const float MouseWheelDeltaPerNotch = 120f;

    private ScreenshotBitmapSourceLoader? _imageLoader;
    private CancellationToken _lifetimeCancellation;
    private CancellationTokenSource? _imageLoadCancellation;
    private string? _currentPath;
    private int _imageLoadGeneration;
    private bool _hasImage;
    private double _imagePixelWidth;
    private double _imagePixelHeight;
    private uint? _dragPointerId;
    private Point _dragStartPosition;
    private double _dragStartHorizontalOffset;
    private double _dragStartVerticalOffset;
    private LocalizationService _strings = new("system");

    /// <summary>Creates the single-image screenshot viewer.</summary>
    public ScreenshotImageViewerControl()
    {
        InitializeComponent();
        ImageScroller.AddHandler(PointerPressedEvent, new PointerEventHandler(ImageScroller_PointerPressed), true);
        ImageScroller.AddHandler(PointerMovedEvent, new PointerEventHandler(ImageScroller_PointerMoved), true);
        ImageScroller.AddHandler(PointerReleasedEvent, new PointerEventHandler(ImageScroller_PointerReleased), true);
        ImageScroller.PointerCanceled += ImageScroller_PointerCanceled;
        ImageScroller.PointerCaptureLost += ImageScroller_PointerCaptureLost;
        UpdateBaseContentSize();
        NotifyZoomStateChanged();
    }

    /// <summary>Raised whenever zoom or selected-image availability changes.</summary>
    public event EventHandler? ZoomStateChanged;

    /// <summary>Raised when the selected retained image cannot be read or decoded.</summary>
    public event EventHandler? ImageLoadFailed;

    /// <summary>Connects the passive viewer to the application-owned screenshot loader.</summary>
    public void Configure(IWorkTrailApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_imageLoader is not null)
        {
            throw new InvalidOperationException("The screenshot viewer is already configured.");
        }

        _imageLoader = new ScreenshotBitmapSourceLoader(application);
        _lifetimeCancellation = cancellationToken;
    }

    /// <summary>Gets the localized current zoom percentage.</summary>
    public string ZoomText => _strings.Format("Screenshots.ZoomPercent", ImageScroller.ZoomFactor);

    /// <summary>Gets whether an image is currently selected.</summary>
    public bool HasImage => _hasImage;

    /// <summary>Gets whether the current image can be zoomed out.</summary>
    public bool CanZoomOut => _hasImage && ImageScroller.ZoomFactor > MinimumZoomFactor + 0.001f;

    /// <summary>Gets whether the current image can be zoomed in.</summary>
    public bool CanZoomIn => _hasImage && ImageScroller.ZoomFactor < MaximumZoomFactor - 0.001f;

    /// <summary>Reduces the zoom around the center of the viewport.</summary>
    public void ZoomOut() => SetZoom(ImageScroller.ZoomFactor - ZoomStep);

    /// <summary>Restores the fitted base zoom.</summary>
    public void ResetZoom() => ResetZoom(disableAnimation: false);

    /// <summary>Increases the zoom around the center of the viewport.</summary>
    public void ZoomIn() => SetZoom(ImageScroller.ZoomFactor + ZoomStep);

    /// <summary>Replaces the currently displayed screenshot without owning gallery selection state.</summary>
    public void SetItem(ScreenshotGalleryItem? item, int selectedIndex, int totalCount, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        _strings = new LocalizationService(language);
        AutomationProperties.SetName(this, _strings.Translate("Screenshots.Caption"));
        if (item is null)
        {
            ClearImage();
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= totalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex), selectedIndex, "The selected screenshot must exist in the viewer source.");
        }

        if (!Path.IsPathFullyQualified(item.Path))
        {
            throw new InvalidDataException("Screenshot path is not fully qualified.");
        }

        if (_imageLoader is null)
        {
            throw new InvalidOperationException("The screenshot viewer must be configured before rendering an item.");
        }

        if (!string.Equals(_currentPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            BeginImageLoad(item.Path);
        }

        var localTime = item.CapturedAt.ToLocalTime();
        AutomationProperties.SetName(
            ScreenshotImage,
            _strings.Format("Screenshots.Image.Accessible", selectedIndex + 1, totalCount, localTime));
        NotifyZoomStateChanged();
    }

    private void ClearImage()
    {
        CancelImageLoad();
        _currentPath = null;
        _hasImage = false;
        _imagePixelWidth = 0d;
        _imagePixelHeight = 0d;
        ScreenshotImage.Source = null;
        ScreenshotFrame.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(ScreenshotImage, _strings.Translate("Screenshots.Image.None"));
        UpdateBaseContentSize();
        NotifyZoomStateChanged();
    }

    private void BeginImageLoad(string screenshotPath)
    {
        CancelImageLoad();
        _currentPath = screenshotPath;
        _hasImage = false;
        _imagePixelWidth = 0d;
        _imagePixelHeight = 0d;
        ScreenshotImage.Source = null;
        ScreenshotFrame.Visibility = Visibility.Collapsed;
        UpdateBaseContentSize();
        NotifyZoomStateChanged();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation);
        _imageLoadCancellation = cancellation;
        var generation = ++_imageLoadGeneration;
        _ = LoadImageAsync(screenshotPath, generation, cancellation);
    }

    private async Task LoadImageAsync(
        string screenshotPath,
        int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var loader = _imageLoader
                ?? throw new InvalidOperationException("The screenshot viewer image loader is unavailable.");
            var result = await loader.LoadAsync(screenshotPath, decodePixelWidth: 0, cancellation.Token);
            if (!IsCurrentImageLoad(screenshotPath, generation, cancellation))
            {
                return;
            }

            if (!result.Succeeded || result.Bitmap is null)
            {
                HandleImageLoadFailure(screenshotPath, generation, cancellation);
                return;
            }

            _hasImage = true;
            ScreenshotImage.Source = result.Bitmap;
            ApplyLoadedBitmapLayout(result.Bitmap);
            NotifyZoomStateChanged();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer selection or the closing window owns the next visible image state.
        }
        catch (Exception)
        {
            if (IsCurrentImageLoad(screenshotPath, generation, cancellation))
            {
                HandleImageLoadFailure(screenshotPath, generation, cancellation);
            }
        }
        finally
        {
            if (ReferenceEquals(_imageLoadCancellation, cancellation))
            {
                _imageLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentImageLoad(
        string screenshotPath,
        int generation,
        CancellationTokenSource cancellation) =>
        !cancellation.IsCancellationRequested
        && generation == _imageLoadGeneration
        && ReferenceEquals(_imageLoadCancellation, cancellation)
        && string.Equals(_currentPath, screenshotPath, StringComparison.OrdinalIgnoreCase);

    private void HandleImageLoadFailure(
        string screenshotPath,
        int generation,
        CancellationTokenSource cancellation)
    {
        if (!IsCurrentImageLoad(screenshotPath, generation, cancellation))
        {
            return;
        }

        _currentPath = null;
        _hasImage = false;
        _imagePixelWidth = 0d;
        _imagePixelHeight = 0d;
        ScreenshotImage.Source = null;
        ScreenshotFrame.Visibility = Visibility.Collapsed;
        UpdateBaseContentSize();
        NotifyZoomStateChanged();
        ImageLoadFailed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelImageLoad()
    {
        _imageLoadGeneration++;
        if (_imageLoadCancellation is not { } cancellation)
        {
            return;
        }

        _imageLoadCancellation = null;
        cancellation.Cancel();
    }

    private void ScreenshotImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (ScreenshotImage.Source is not BitmapImage bitmap)
        {
            return;
        }

        ApplyLoadedBitmapLayout(bitmap);
    }

    private void ApplyLoadedBitmapLayout(BitmapImage bitmap)
    {
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return;
        }

        _imagePixelWidth = bitmap.PixelWidth;
        _imagePixelHeight = bitmap.PixelHeight;
        ScreenshotFrame.Visibility = Visibility.Visible;
        UpdateBaseContentSize();
        if (!DispatcherQueue.TryEnqueue(() => ResetZoom(disableAnimation: true)))
        {
            ResetZoom(disableAnimation: true);
        }
    }

    private void ImageHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageScroller);
        if (!_hasImage
            || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse
            || point.Properties.IsHorizontalMouseWheel
            || point.Properties.MouseWheelDelta == 0)
        {
            return;
        }

        var target = ImageScroller.ZoomFactor
            + ((point.Properties.MouseWheelDelta / MouseWheelDeltaPerNotch) * ZoomStep);
        SetZoom(target, point.Position, disableAnimation: true);
        e.Handled = true;
    }

    private void SetZoom(float zoomFactor)
    {
        var (viewportWidth, viewportHeight) = GetViewportSize();
        SetZoom(
            zoomFactor,
            new Point(viewportWidth / 2d, viewportHeight / 2d),
            disableAnimation: false);
    }

    private void SetZoom(float zoomFactor, Point viewportAnchor, bool disableAnimation)
    {
        if (!_hasImage || !IsLoaded)
        {
            return;
        }

        var target = Math.Clamp(zoomFactor, MinimumZoomFactor, MaximumZoomFactor);
        var currentZoom = Math.Max(MinimumZoomFactor, ImageScroller.ZoomFactor);
        var (viewportWidth, viewportHeight) = GetViewportSize();
        var anchorX = Math.Clamp(viewportAnchor.X, 0d, viewportWidth);
        var anchorY = Math.Clamp(viewportAnchor.Y, 0d, viewportHeight);
        var contentAnchorX = (ImageScroller.HorizontalOffset + anchorX) / currentZoom;
        var contentAnchorY = (ImageScroller.VerticalOffset + anchorY) / currentZoom;
        var horizontalOffset = Math.Clamp(
            (contentAnchorX * target) - anchorX,
            0d,
            Math.Max(0d, (ImageHost.Width * target) - viewportWidth));
        var verticalOffset = Math.Clamp(
            (contentAnchorY * target) - anchorY,
            0d,
            Math.Max(0d, (ImageHost.Height * target) - viewportHeight));
        ImageScroller.ChangeView(horizontalOffset, verticalOffset, target, disableAnimation);
        NotifyZoomStateChanged();
    }

    private void ResetZoom(bool disableAnimation)
    {
        _dragPointerId = null;
        ImageScroller.ReleasePointerCaptures();
        UpdateBaseContentSize();
        if (!_hasImage || !IsLoaded)
        {
            NotifyZoomStateChanged();
            return;
        }

        var (viewportWidth, viewportHeight) = GetViewportSize();
        ImageScroller.ChangeView(
            Math.Max(0d, (ImageHost.Width - viewportWidth) / 2d),
            Math.Max(0d, (ImageHost.Height - viewportHeight) / 2d),
            MinimumZoomFactor,
            disableAnimation);
        NotifyZoomStateChanged();
    }

    private void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBaseContentSize();
        if (!_hasImage || !IsLoaded)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(CenterCurrentView))
        {
            CenterCurrentView();
        }
    }

    private void ImageScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
        NotifyZoomStateChanged();

    private void UpdateBaseContentSize()
    {
        var (viewportWidth, viewportHeight) = GetViewportSize();
        if (!_hasImage || _imagePixelWidth <= 0d || _imagePixelHeight <= 0d)
        {
            ImageHost.Width = viewportWidth;
            ImageHost.Height = viewportHeight;
            ScreenshotFrame.Width = Math.Max(1d, viewportWidth - (ScreenshotFrameMargin * 2d));
            ScreenshotFrame.Height = Math.Max(1d, viewportHeight - (ScreenshotFrameMargin * 2d));
            return;
        }

        var availableWidth = Math.Max(1d, viewportWidth - (ScreenshotFrameMargin * 2d));
        var availableHeight = Math.Max(1d, viewportHeight - (ScreenshotFrameMargin * 2d));
        var containScale = Math.Min(availableWidth / _imagePixelWidth, availableHeight / _imagePixelHeight);
        var imageWidth = _imagePixelWidth * containScale;
        var imageHeight = _imagePixelHeight * containScale;
        ScreenshotFrame.Width = imageWidth;
        ScreenshotFrame.Height = imageHeight;
        ImageHost.Width = Math.Max(viewportWidth, ScreenshotFrame.Width + (ScreenshotFrameMargin * 2d));
        ImageHost.Height = Math.Max(viewportHeight, ScreenshotFrame.Height + (ScreenshotFrameMargin * 2d));
    }

    private (double Width, double Height) GetViewportSize() =>
        (Math.Max(1d, ImageScroller.ActualWidth), Math.Max(1d, ImageScroller.ActualHeight));

    private void CenterCurrentView()
    {
        if (!_hasImage || !IsLoaded)
        {
            return;
        }

        var zoom = Math.Max(MinimumZoomFactor, ImageScroller.ZoomFactor);
        var (viewportWidth, viewportHeight) = GetViewportSize();
        ImageScroller.ChangeView(
            Math.Max(0d, ((ImageHost.Width * zoom) - viewportWidth) / 2d),
            Math.Max(0d, ((ImageHost.Height * zoom) - viewportHeight) / 2d),
            null,
            disableAnimation: true);
    }

    private void ImageScroller_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ImageScroller);
        if (!_hasImage
            || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse
            || !point.Properties.IsLeftButtonPressed
            || (ImageScroller.ScrollableWidth <= 0.5d && ImageScroller.ScrollableHeight <= 0.5d)
            || !ImageScroller.CapturePointer(e.Pointer))
        {
            return;
        }

        _dragPointerId = e.Pointer.PointerId;
        _dragStartPosition = point.Position;
        _dragStartHorizontalOffset = ImageScroller.HorizontalOffset;
        _dragStartVerticalOffset = ImageScroller.VerticalOffset;
        e.Handled = true;
    }

    private void ImageScroller_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(ImageScroller);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragPointerId = null;
            ImageScroller.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            return;
        }

        var horizontalOffset = Math.Clamp(
            _dragStartHorizontalOffset - (point.Position.X - _dragStartPosition.X),
            0d,
            ImageScroller.ScrollableWidth);
        var verticalOffset = Math.Clamp(
            _dragStartVerticalOffset - (point.Position.Y - _dragStartPosition.Y),
            0d,
            ImageScroller.ScrollableHeight);
        ImageScroller.ChangeView(horizontalOffset, verticalOffset, null, disableAnimation: true);
        e.Handled = true;
    }

    private void ImageScroller_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
        {
            return;
        }

        _dragPointerId = null;
        ImageScroller.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ImageScroller_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        _dragPointerId = null;

    private void ImageScroller_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _dragPointerId = null;

    private void NotifyZoomStateChanged() => ZoomStateChanged?.Invoke(this, EventArgs.Empty);
}
