// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Diagnostics;
using WorkTrail.Presentation;
using WorkTrail.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WorkTrail;

/// <summary>Owns the native/XAML contract shared by WorkTrail custom title bars.</summary>
internal sealed class CustomTitleBarController : IDisposable
{
    private static readonly HashSet<CustomTitleBarController> Controllers = [];
    private static bool _autoHideEnabled = true;
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly FrameworkElement _root;
    private readonly FrameworkElement _dragRegion;
    private readonly ColumnDefinition _leftInsetColumn;
    private readonly ColumnDefinition _rightInsetColumn;
    private readonly Func<IEnumerable<FrameworkElement>> _interactiveElements;
    private readonly Grid _rootGrid;
    private readonly bool _supportsContentOverlay;
    private readonly bool _allowAutoHide;
    private TitleBarOverlayLayout? _overlayLayout;
    private bool _overlayContentEnabled;
    private readonly Border _revealSurface;
    private readonly InputNonClientPointerSource _pointerSource;
    private readonly DispatcherQueueTimer _pointerTimer;
    private readonly DispatcherQueueTimer _transitionTimer;
    private readonly TitleBarVisibilityState _visibility = new();
    private readonly TitleBarTransitionState _transition = new();
    private readonly long _transitionOrigin = Stopwatch.GetTimestamp();
    private Storyboard? _fadeStoryboard;
    private readonly TitleBarHeightOption _visibleHeight;
    private readonly IntPtr _windowHandle;
    private readonly double _visibleOpacity;
    private readonly bool _visibleHitTesting;
    private bool _chromeVisible = true;
    private bool _isWindowActive;
    private DateTimeOffset? _touchRevealUntil;
    private uint? _revealPointerId;
    private XamlRoot? _xamlRoot;
    private bool _layoutUpdateQueued;
    private bool _disposed;

    // window hosts the custom caption.
    // appWindow supplies the native caption and pointer regions.
    // root contains the title bar and window content.
    // dragRegion defines the visible title bar and native drag area.
    // leftInsetColumn reserves the native left caption inset.
    // rightInsetColumn reserves space for native caption buttons.
    // interactiveElements identifies title-bar controls that receive pointer input.
    // useTallTitleBar selects the native caption height.
    // overlayContent allows the caption to float above content when hidden automatically.
    // allowAutoHide lets this window follow the global auto-hide preference.
    internal CustomTitleBarController(
        Window window,
        AppWindow appWindow,
        FrameworkElement root,
        FrameworkElement dragRegion,
        ColumnDefinition leftInsetColumn,
        ColumnDefinition rightInsetColumn,
        Func<IEnumerable<FrameworkElement>> interactiveElements,
        bool useTallTitleBar = true,
        bool overlayContent = false,
        bool allowAutoHide = true)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _dragRegion = dragRegion ?? throw new ArgumentNullException(nameof(dragRegion));
        _leftInsetColumn = leftInsetColumn ?? throw new ArgumentNullException(nameof(leftInsetColumn));
        _rightInsetColumn = rightInsetColumn ?? throw new ArgumentNullException(nameof(rightInsetColumn));
        _interactiveElements = interactiveElements ?? throw new ArgumentNullException(nameof(interactiveElements));
        _rootGrid = root as Grid
            ?? throw new ArgumentException("Shared title bars require a grid window root.", nameof(root));
        _overlayContentEnabled = overlayContent;
        _supportsContentOverlay = overlayContent;
        _allowAutoHide = allowAutoHide;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _visibleOpacity = _dragRegion.Opacity;
        _visibleHitTesting = _dragRegion.IsHitTestVisible;
        _visibleHeight = useTallTitleBar ? TitleBarHeightOption.Tall : TitleBarHeightOption.Standard;
        _pointerSource = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        _pointerTimer = _root.DispatcherQueue.CreateTimer();
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(200);
        _pointerTimer.Tick += PointerTimer_Tick;
        _transitionTimer = _root.DispatcherQueue.CreateTimer();
        _transitionTimer.IsRepeating = false;
        _transitionTimer.Tick += TransitionTimer_Tick;
        _revealSurface = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAccessibilityView(_revealSurface, AccessibilityView.Raw);
        // Shield only the title-bar row: normal content must accept the first click during the hover delay.
        Grid.SetRow(_revealSurface, 0);
        Grid.SetColumnSpan(_revealSurface, Math.Max(1, _rootGrid.ColumnDefinitions.Count));
        // WinUI validates ZIndex against a bounded range; keep the input shield above ordinary content.
        Canvas.SetZIndex(_revealSurface, 1000);
        _rootGrid.Children.Add(_revealSurface);
        _revealSurface.PointerPressed += RevealSurface_PointerPressed;
        _revealSurface.PointerReleased += RevealSurface_PointerReleased;
        _revealSurface.PointerCanceled += RevealSurface_PointerCanceled;
        _revealSurface.PointerCaptureLost += RevealSurface_PointerCaptureLost;

        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(_dragRegion);
        if (useTallTitleBar && AppWindowTitleBar.IsCustomizationSupported())
        {
            // Tall is the native 48-DIP caption height used by every WorkTrail title-bar grid.
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }

        _root.ActualThemeChanged += Root_ActualThemeChanged;
        _root.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(Root_PointerHovered), handledEventsToo: true);
        _root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Root_PointerHovered), handledEventsToo: true);
        _root.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Root_PointerExited), handledEventsToo: true);
        _root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), handledEventsToo: true);
        _root.PreviewKeyDown += Root_PreviewKeyDown;
        _root.GotFocus += Root_GotFocus;
        _window.Activated += Window_Activated;
        _appWindow.Changed += AppWindow_Changed;
        _pointerSource.PointerEntered += NonClient_PointerHovered;
        _pointerSource.PointerMoved += NonClient_PointerHovered;
        _pointerSource.PointerExited += NonClient_PointerExited;
        _pointerSource.PointerReleased += NonClient_PointerReleased;
        _dragRegion.Loaded += DragRegion_Loaded;
        _dragRegion.SizeChanged += DragRegion_SizeChanged;
        ApplyOverlayLayout();
        ApplyTheme(_root.ActualTheme);
        Controllers.Add(this);
    }

    /// <summary>Applies the persisted global preference to both existing and subsequently created title bars.</summary>
    internal static void ApplyAutoHideSetting(bool enabled)
    {
        if (_autoHideEnabled == enabled)
        {
            return;
        }

        _autoHideEnabled = enabled;
        foreach (var controller in Controllers.ToArray())
        {
            controller.ApplyOverlayLayout();
            controller.ApplyChromeVisibility(immediate: !enabled);
        }
    }

    /// <summary>Gets the vertical space consumed by docked chrome, excluding a floating overlay.</summary>
    internal double ReservedHeight => _overlayLayout?.ReservedHeight
        ?? (_supportsContentOverlay && _overlayContentEnabled && AutoHideEnabled ? 0d : _dragRegion.ActualHeight);

    private bool AutoHideEnabled => _allowAutoHide && _autoHideEnabled;

    /// <summary>Temporarily docks the title bar when a widget displays interactive settings.</summary>
    internal void SetOverlayContentEnabled(bool enabled)
    {
        if (!_supportsContentOverlay)
        {
            throw new InvalidOperationException("This title bar was not configured for content overlays.");
        }

        _overlayContentEnabled = enabled;
        ApplyOverlayLayout();
    }

    private void ApplyOverlayLayout()
    {
        if (!_supportsContentOverlay || !_dragRegion.IsLoaded)
        {
            return;
        }

        // WinUI does not attach XAML parents during window construction. Capture the original
        // rows only after Loaded, when the header belongs to its live visual tree.
        _overlayLayout ??= new TitleBarOverlayLayout(_rootGrid, _dragRegion);
        var overlay = _overlayContentEnabled && AutoHideEnabled;
        _overlayLayout.SetEnabled(overlay);
        // Span the content only for layout; the shield's fixed height limits input to the floating caption.
        Grid.SetRowSpan(_revealSurface, overlay ? Math.Max(1, _rootGrid.RowDefinitions.Count) : 1);
        _revealSurface.Height = overlay ? _overlayLayout.HeaderHeight : double.NaN;
        _revealSurface.VerticalAlignment = overlay ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        QueueLayoutUpdate();
    }

    /// <summary>Occurs after native caption colors have followed a XAML theme change.</summary>
    internal event Action<ElementTheme>? ThemeChanged;

    /// <summary>Applies the shared light, dark, or high-contrast native caption palette.</summary>
    internal void ApplyTheme(ElementTheme effectiveTheme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var palette = ResolvePalette(effectiveTheme);
        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = palette.Foreground;
        titleBar.ButtonInactiveForegroundColor = palette.InactiveForeground;
        titleBar.ButtonHoverForegroundColor = palette.HoverForeground;
        titleBar.ButtonPressedForegroundColor = palette.PressedForeground;
        titleBar.ButtonHoverBackgroundColor = palette.HoverBackground;
        titleBar.ButtonPressedBackgroundColor = palette.PressedBackground;
    }

    /// <summary>Queues one post-layout refresh of caption insets and interactive regions.</summary>
    internal void QueueLayoutUpdate()
    {
        if (_disposed || _layoutUpdateQueued)
        {
            return;
        }

        _layoutUpdateQueued = true;
        if (!_root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _layoutUpdateQueued = false;
                if (_disposed || !_root.IsLoaded)
                {
                    return;
                }

                // Flush pending XAML layout before deriving window-relative physical rectangles.
                _root.UpdateLayout();
                UpdateLayout();
            }))
        {
            _layoutUpdateQueued = false;
        }
    }

    private void DragRegion_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyOverlayLayout();
        AttachXamlRoot(_dragRegion.XamlRoot);
        UpdateLayout();
        _pointerTimer.Start();
        RefreshPointerVisibility();
    }

    private void PointerTimer_Tick(DispatcherQueueTimer sender, object args) => RefreshPointerVisibility();

    private void TransitionTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        // Recheck the actual pointer before committing a delayed transition over native or WebView content.
        RefreshPointerVisibility();
    }

    private void RefreshPointerVisibility()
    {
        if (_disposed || !_root.IsLoaded || !_appWindow.IsVisible)
        {
            return;
        }

        if (_touchRevealUntil is { } until && DateTimeOffset.UtcNow >= until && !HasKeyboardFocusInTitleBar())
        {
            _touchRevealUntil = null;
            _visibility.EndInteraction();
        }

        var pointer = WindowPointerService.Read(_windowHandle);
        if (!pointer.IsAvailable)
        {
            // The secure desktop owns input; retain current chrome until normal desktop input resumes.
            return;
        }

        _visibility.UpdatePointer(pointer.IsInside, pointer.IsPressed);
        ApplyChromeVisibility();
    }

    private void Root_PointerHovered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse
            && (args.Pointer.PointerDeviceType != PointerDeviceType.Pen || args.GetCurrentPoint(_root).IsInContact))
        {
            // A contact has no preceding hover. Keep the shield in place until its first release.
            return;
        }

        _touchRevealUntil = null;
        _visibility.ResumeHover(inside: true, pressed: args.GetCurrentPoint(_root).IsInContact);
        ApplyChromeVisibility();
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
        {
            // Client-to-caption transitions also raise Exited; the native query distinguishes a true window exit.
            RefreshPointerVisibility();
        }
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse && !_visibility.IsRevealContactPending)
        {
            RevealForTouch();
        }
    }

    private void NonClient_PointerHovered(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        if (args.PointerDeviceType == PointerDeviceType.Mouse)
        {
            _touchRevealUntil = null;
            _visibility.ResumeHover(inside: true, pressed: false);
            ApplyChromeVisibility();
        }
    }

    private void NonClient_PointerExited(InputNonClientPointerSource sender, NonClientPointerEventArgs args) =>
        RefreshPointerVisibility();

    private void NonClient_PointerReleased(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        if (args.PointerDeviceType != PointerDeviceType.Mouse)
        {
            RevealForTouch();
        }
    }

    private void RevealSurface_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (_visibility.BeginRevealContact(_chromeVisible))
        {
            _revealPointerId = args.Pointer.PointerId;
            if (!_revealSurface.CapturePointer(args.Pointer))
            {
                // Failed capture cannot safely enable a command beneath this still-active contact.
                _revealPointerId = null;
                _visibility.EndInteraction();
                throw new InvalidOperationException("Unable to capture the title-bar reveal contact.");
            }
        }
    }

    private void RevealSurface_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        if (_revealPointerId != args.Pointer.PointerId)
        {
            return;
        }

        _revealPointerId = null;
        _revealSurface.ReleasePointerCapture(args.Pointer);
        // Finish the input route before exposing native or XAML commands under the released contact.
        if (!_root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!_disposed && _visibility.IsRevealContactPending)
                {
                    _visibility.CompleteRevealContact();
                    RevealForTouch();
                }
            }))
        {
            throw new InvalidOperationException("Unable to complete the title-bar reveal interaction.");
        }
    }

    private void RevealSurface_PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        CancelRevealContact(args.Pointer.PointerId);
    }

    private void RevealSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        CancelRevealContact(args.Pointer.PointerId);

    private void CancelRevealContact(uint pointerId)
    {
        if (_revealPointerId == pointerId)
        {
            _revealPointerId = null;
            _visibility.EndInteraction();
            RefreshPointerVisibility();
        }
    }

    private void RevealForTouch()
    {
        _touchRevealUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        _visibility.RevealForInteraction();
        ApplyChromeVisibility(immediate: true);
    }

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        _touchRevealUntil = null;
        _visibility.RevealForInteraction();
        ApplyChromeVisibility(immediate: true);
    }

    private void Root_GotFocus(object sender, RoutedEventArgs args)
    {
        if (HasKeyboardFocusInTitleBar())
        {
            _touchRevealUntil = null;
            _visibility.RevealForInteraction();
            ApplyChromeVisibility(immediate: true);
        }
    }

    private bool HasKeyboardFocusInTitleBar()
    {
        if (_xamlRoot is null
            || FocusManager.GetFocusedElement(_xamlRoot) is not Control { FocusState: FocusState.Keyboard or FocusState.Programmatic } focused)
        {
            return false;
        }

        for (DependencyObject? element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, _dragRegion))
            {
                return true;
            }
        }

        return false;
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _touchRevealUntil = null;
            _revealPointerId = null;
            _revealSurface.ReleasePointerCaptures();
            _visibility.EndInteraction();
        }

        RefreshPointerVisibility();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange)
        {
            if (sender.IsVisible)
            {
                _pointerTimer.Start();
                RefreshPointerVisibility();
            }
            else
            {
                _pointerTimer.Stop();
                _transitionTimer.Stop();
            }
        }
    }

    private void ApplyChromeVisibility(bool immediate = false)
    {
        if (_disposed)
        {
            return;
        }

        // Popups can use a separate HWND; changing chrome while a flyout is open hides its anchor.
        // Keyboard and automation focus must also remain visible when the mouse moves elsewhere.
        var protectedInteraction = (_isWindowActive && HasKeyboardFocusInTitleBar())
            || (_xamlRoot is not null && VisualTreeHelper.GetOpenPopupsForXamlRoot(_xamlRoot).Count > 0);
        _visibility.SetInteractionProtection(protectedInteraction);
        // A contact that began on hidden chrome must finish before any delayed or settings-driven reveal.
        var desiredVisible = !_allowAutoHide
            || (!_visibility.IsRevealContactPending && (!_autoHideEnabled || _visibility.ShouldShowChrome(_chromeVisible)));
        var visible = _transition.Update(desiredVisible, Stopwatch.GetElapsedTime(_transitionOrigin),
            immediate: immediate || protectedInteraction || !AutoHideEnabled);
        _transitionTimer.Stop();
        if (_transition.HasPendingTransition && _root.IsLoaded && _appWindow.IsVisible)
        {
            _transitionTimer.Interval = _transition.RemainingDelay < TimeSpan.FromMilliseconds(1)
                ? TimeSpan.FromMilliseconds(1) : _transition.RemainingDelay;
            _transitionTimer.Start();
        }

        if (_chromeVisible == visible)
        {
            return;
        }

        _chromeVisible = visible;
        // Hover changes only chrome and hit regions; the content and window bounds retain their layout.
        FadeChrome(visible);
        _dragRegion.IsHitTestVisible = visible && _visibleHitTesting;
        _revealSurface.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        _window.SetTitleBar(visible ? _dragRegion : null);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            // Collapsed removes native caption buttons; transparent caption colors do not hide system glyphs.
            _appWindow.TitleBar.PreferredHeightOption = visible ? _visibleHeight : TitleBarHeightOption.Collapsed;
        }

        QueueLayoutUpdate();
    }

    private void FadeChrome(bool visible)
    {
        // Read the animated value before stopping so a reversal continues without an opacity jump.
        var currentOpacity = _dragRegion.Opacity;
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
        var targetOpacity = visible ? _visibleOpacity : 0d;
        _dragRegion.Opacity = targetOpacity;
        if (!_root.IsLoaded || !new UISettings().AnimationsEnabled)
        {
            // Respect Windows' reduced-motion preference while keeping visibility and hit testing in sync.
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentOpacity,
            To = targetOpacity,
            Duration = new Duration(TitleBarTransitionState.FadeDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, _dragRegion);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _fadeStoryboard = new Storyboard();
        _fadeStoryboard.Children.Add(animation);
        _fadeStoryboard.Begin();
    }

    private void DragRegion_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayout();

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTheme(sender.ActualTheme);
        ThemeChanged?.Invoke(sender.ActualTheme);
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => QueueLayoutUpdate();

    private void AttachXamlRoot(XamlRoot? xamlRoot)
    {
        if (ReferenceEquals(_xamlRoot, xamlRoot))
        {
            return;
        }

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= XamlRoot_Changed;
        }

        _xamlRoot = xamlRoot;
        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed += XamlRoot_Changed;
        }
    }

    private void UpdateLayout()
    {
        if (_disposed || !_window.ExtendsContentIntoTitleBar || _dragRegion.XamlRoot is not { } xamlRoot)
        {
            return;
        }

        AttachXamlRoot(xamlRoot);
        if (!_chromeVisible)
        {
            _pointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
            return;
        }

        var scale = Math.Max(0.1d, xamlRoot.RasterizationScale);
        if (_appWindow.Presenter is OverlappedPresenter { HasTitleBar: false })
        {
            // The borderless player owns its caption buttons in XAML and reserves no native caption area.
            // Native caption insets are not valid layout inputs after the system title bar is removed.
            _leftInsetColumn.Width = new GridLength(0);
            _rightInsetColumn.Width = new GridLength(0);
        }
        else
        {
            _leftInsetColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset / scale);
            _rightInsetColumn.Width = new GridLength(_appWindow.TitleBar.RightInset / scale);
        }

        // Insets can move commands at a new DPI; commit their layout before calculating hit regions.
        _root.UpdateLayout();
        var passthroughRects = _interactiveElements()
            .Where(static element =>
                element.Visibility == Visibility.Visible
                && element.IsHitTestVisible
                && element.ActualWidth > 0d
                && element.ActualHeight > 0d)
            .Distinct()
            .Select(element => ElementRect(element, scale))
            .Where(static rect => rect.Width > 0 && rect.Height > 0)
            .ToArray();

        var pointerSource = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        if (passthroughRects.Length == 0)
        {
            // WinAppSDK rejects an empty SetRegionRects payload; clearing also removes stale passthrough regions.
            pointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
            return;
        }

        // The OS retains dragging and caption buttons; only explicit XAML commands pass through.
        pointerSource.SetRegionRects(NonClientRegionKind.Passthrough, passthroughRects);
    }

    private static RectInt32 ElementRect(FrameworkElement element, double scale)
    {
        var transform = element.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    private static TitleBarPalette ResolvePalette(ElementTheme effectiveTheme)
    {
        if (new AccessibilitySettings().HighContrast)
        {
            var settings = new UISettings();
            var foreground = settings.GetColorValue(UIColorType.Foreground);
            var background = settings.GetColorValue(UIColorType.Background);
            var accent = settings.GetColorValue(UIColorType.Accent);
            return new TitleBarPalette(foreground, foreground, background, background, accent, accent);
        }

        if (effectiveTheme == ElementTheme.Dark)
        {
            return new TitleBarPalette(
                Colors.White,
                Color.FromArgb(160, 255, 255, 255),
                Colors.White,
                Colors.White,
                Color.FromArgb(32, 255, 255, 255),
                Color.FromArgb(48, 255, 255, 255));
        }

        return new TitleBarPalette(
            Colors.Black,
            Color.FromArgb(160, 0, 0, 0),
            Colors.Black,
            Colors.Black,
            Color.FromArgb(24, 0, 0, 0),
            Color.FromArgb(40, 0, 0, 0));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Controllers.Remove(this);
        _transitionTimer.Stop();
        _transitionTimer.Tick -= TransitionTimer_Tick;
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
        _pointerTimer.Stop();
        _pointerTimer.Tick -= PointerTimer_Tick;
        _window.Activated -= Window_Activated;
        _appWindow.Changed -= AppWindow_Changed;
        _pointerSource.PointerEntered -= NonClient_PointerHovered;
        _pointerSource.PointerMoved -= NonClient_PointerHovered;
        _pointerSource.PointerExited -= NonClient_PointerExited;
        _pointerSource.PointerReleased -= NonClient_PointerReleased;
        _root.RemoveHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(Root_PointerHovered));
        _root.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Root_PointerHovered));
        _root.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(Root_PointerExited));
        _root.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed));
        _root.PreviewKeyDown -= Root_PreviewKeyDown;
        _root.GotFocus -= Root_GotFocus;
        _revealSurface.PointerPressed -= RevealSurface_PointerPressed;
        _revealSurface.PointerReleased -= RevealSurface_PointerReleased;
        _revealSurface.PointerCanceled -= RevealSurface_PointerCanceled;
        _revealSurface.PointerCaptureLost -= RevealSurface_PointerCaptureLost;
        _rootGrid.Children.Remove(_revealSurface);
        _root.ActualThemeChanged -= Root_ActualThemeChanged;
        _dragRegion.Loaded -= DragRegion_Loaded;
        _dragRegion.SizeChanged -= DragRegion_SizeChanged;
        AttachXamlRoot(null);
        ThemeChanged = null;
    }

    private sealed record TitleBarPalette(
        Color Foreground,
        Color InactiveForeground,
        Color HoverForeground,
        Color PressedForeground,
        Color HoverBackground,
        Color PressedBackground);
}
