// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Describes visible window edges in physical desktop pixels, including negative monitor coordinates.</summary>
public readonly record struct WindowSnapRectangle(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Gets the horizontal extent; unrepresentable extents fail instead of overflowing.</summary>
    public int Width => checked(Right - Left);

    /// <summary>Gets the vertical extent; unrepresentable extents fail instead of overflowing.</summary>
    public int Height => checked(Bottom - Top);
}

/// <summary>Describes a nearby alignment in physical desktop pixels and whether it currently snaps.</summary>
public readonly record struct WindowSnapGuide(int Coordinate, bool IsSnapped);

/// <summary>Identifies the moving edges of a native resize operation.</summary>
[Flags]
public enum WindowSnapEdges
{
    /// <summary>No edges are moving.</summary>
    None = 0,
    /// <summary>The left edge is moving.</summary>
    Left = 1,
    /// <summary>The top edge is moving.</summary>
    Top = 2,
    /// <summary>The right edge is moving.</summary>
    Right = 4,
    /// <summary>The bottom edge is moving.</summary>
    Bottom = 8
}

/// <summary>Calculates DPI-scaled edge snapping for movement and resizing on the starting monitor.</summary>
public sealed class WindowSnapSession
{
    private int _snapDistance;
    private int _guideDistance;
    private readonly WindowSnapRectangle _monitorBounds;
    private readonly WindowSnapRectangle _workArea;

    /// <summary>Starts an operation on a physical monitor using ten/twenty logical-pixel snap/guide distances at the supplied DPI.</summary>
    public WindowSnapSession(WindowSnapRectangle monitorBounds, WindowSnapRectangle workArea, uint dpi = 96)
    {
        Validate(monitorBounds, nameof(monitorBounds));
        Validate(workArea, nameof(workArea));
        if (!Contains(monitorBounds, workArea))
        {
            throw new ArgumentException("The work area must be contained within the monitor bounds.", nameof(workArea));
        }

        _monitorBounds = monitorBounds;
        _workArea = workArea;
        UpdateDpi(dpi);
    }

    /// <summary>Updates physical thresholds after a DPI change without resetting an escaped operation.</summary>
    public void UpdateDpi(uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        var snapDistance = checked((int)Math.Ceiling(10d * dpi / 96d));
        var guideDistance = checked((int)Math.Ceiling(20d * dpi / 96d));
        _snapDistance = snapDistance;
        _guideDistance = guideDistance;
    }

    /// <summary>Gets whether a raw proposal exceeded the monitor's DPI-scaled snap margin, disabling snap until a new session.</summary>
    public bool IsSuppressed { get; private set; }

    /// <summary>Gets whether the last valid move matched an eligible edge, including an exact zero-distance match.</summary>
    public bool IsSnapped { get; private set; }

    /// <summary>Gets the starting monitor's physical bounds for clipping transient guides.</summary>
    public WindowSnapRectangle MonitorBounds => _monitorBounds;

    /// <summary>Gets the vertical guide for the selected horizontal alignment, if nearby.</summary>
    public WindowSnapGuide? VerticalGuide { get; private set; }

    /// <summary>Gets the horizontal guide for the selected vertical alignment, if nearby.</summary>
    public WindowSnapGuide? HorizontalGuide { get; private set; }

    /// <summary>
    /// Translates raw visible bounds toward nearby work-area or peer edges without resizing or clamping.
    /// Peers must overlap or lie within the DPI-scaled snap distance on the perpendicular axis. Equal-distance targets
    /// resolve toward the smaller desktop coordinate, independently of peer order. Every input is validated,
    /// including after suppression; callers must pass unsnapped bounds to avoid making an edge sticky.
    /// </summary>
    public WindowSnapRectangle Move(WindowSnapRectangle proposedBounds, IReadOnlyList<WindowSnapRectangle> peerBounds)
    {
        if (!Prepare(proposedBounds, peerBounds)) return proposedBounds;

        Alignment? horizontal = null;
        Alignment? vertical = null;
        Consider(ref horizontal, _workArea.Left, proposedBounds.Left,
            proposedBounds.Left, proposedBounds.Right, _monitorBounds.Left, _monitorBounds.Right);
        Consider(ref horizontal, _workArea.Right, proposedBounds.Right,
            proposedBounds.Left, proposedBounds.Right, _monitorBounds.Left, _monitorBounds.Right);
        Consider(ref vertical, _workArea.Top, proposedBounds.Top,
            proposedBounds.Top, proposedBounds.Bottom, _monitorBounds.Top, _monitorBounds.Bottom);
        Consider(ref vertical, _workArea.Bottom, proposedBounds.Bottom,
            proposedBounds.Top, proposedBounds.Bottom, _monitorBounds.Top, _monitorBounds.Bottom);

        foreach (var peer in peerBounds)
        {
            if (NearIntervals(proposedBounds.Top, proposedBounds.Bottom, peer.Top, peer.Bottom))
            {
                ConsiderEdges(ref horizontal, proposedBounds.Left, proposedBounds.Right,
                    peer.Left, peer.Right, _monitorBounds.Left, _monitorBounds.Right);
            }

            if (NearIntervals(proposedBounds.Left, proposedBounds.Right, peer.Left, peer.Right))
            {
                ConsiderEdges(ref vertical, proposedBounds.Top, proposedBounds.Bottom,
                    peer.Top, peer.Bottom, _monitorBounds.Top, _monitorBounds.Bottom);
            }
        }

        SetGuides(horizontal, vertical);
        var deltaX = VerticalGuide is { IsSnapped: true } ? horizontal!.Value.Delta : 0;
        var deltaY = HorizontalGuide is { IsSnapped: true } ? vertical!.Value.Delta : 0;
        return new WindowSnapRectangle(
            checked(proposedBounds.Left + deltaX), checked(proposedBounds.Top + deltaY),
            checked(proposedBounds.Right + deltaX), checked(proposedBounds.Bottom + deltaY));
    }

    /// <summary>Snaps only moving resize edges, retaining opposite edges and respecting visible minimum/maximum extents.</summary>
    public WindowSnapRectangle Resize(WindowSnapRectangle proposedBounds, IReadOnlyList<WindowSnapRectangle> peerBounds,
        WindowSnapEdges edges, int minimumWidth = 1, int minimumHeight = 1,
        int maximumWidth = int.MaxValue, int maximumHeight = int.MaxValue)
    {
        if (edges == WindowSnapEdges.None || (edges & ~(WindowSnapEdges.Left | WindowSnapEdges.Top | WindowSnapEdges.Right | WindowSnapEdges.Bottom)) != 0
            || (edges & (WindowSnapEdges.Left | WindowSnapEdges.Right)) == (WindowSnapEdges.Left | WindowSnapEdges.Right)
            || (edges & (WindowSnapEdges.Top | WindowSnapEdges.Bottom)) == (WindowSnapEdges.Top | WindowSnapEdges.Bottom))
            throw new ArgumentOutOfRangeException(nameof(edges));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumHeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumWidth, minimumWidth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumHeight, minimumHeight);
        if (!Prepare(proposedBounds, peerBounds)) return proposedBounds;

        var moveLeft = (edges & WindowSnapEdges.Left) != 0;
        var moveTop = (edges & WindowSnapEdges.Top) != 0;
        var horizontalEdge = moveLeft ? proposedBounds.Left : proposedBounds.Right;
        var verticalEdge = moveTop ? proposedBounds.Top : proposedBounds.Bottom;
        var fixedHorizontalEdge = moveLeft ? proposedBounds.Right : proposedBounds.Left;
        var fixedVerticalEdge = moveTop ? proposedBounds.Bottom : proposedBounds.Top;
        Alignment? horizontal = null;
        Alignment? vertical = null;
        var resizeHorizontal = (edges & (WindowSnapEdges.Left | WindowSnapEdges.Right)) != 0;
        var resizeVertical = (edges & (WindowSnapEdges.Top | WindowSnapEdges.Bottom)) != 0;
        if (resizeHorizontal)
        {
            ConsiderResize(ref horizontal, _workArea.Left, horizontalEdge, fixedHorizontalEdge, moveLeft,
                minimumWidth, maximumWidth, _monitorBounds.Left, _monitorBounds.Right);
            ConsiderResize(ref horizontal, _workArea.Right, horizontalEdge, fixedHorizontalEdge, moveLeft,
                minimumWidth, maximumWidth, _monitorBounds.Left, _monitorBounds.Right);
        }
        if (resizeVertical)
        {
            ConsiderResize(ref vertical, _workArea.Top, verticalEdge, fixedVerticalEdge, moveTop,
                minimumHeight, maximumHeight, _monitorBounds.Top, _monitorBounds.Bottom);
            ConsiderResize(ref vertical, _workArea.Bottom, verticalEdge, fixedVerticalEdge, moveTop,
                minimumHeight, maximumHeight, _monitorBounds.Top, _monitorBounds.Bottom);
        }

        foreach (var peer in peerBounds)
        {
            if (resizeHorizontal && NearIntervals(proposedBounds.Top, proposedBounds.Bottom, peer.Top, peer.Bottom))
            {
                ConsiderResize(ref horizontal, peer.Left, horizontalEdge, fixedHorizontalEdge, moveLeft,
                    minimumWidth, maximumWidth, _monitorBounds.Left, _monitorBounds.Right);
                ConsiderResize(ref horizontal, peer.Right, horizontalEdge, fixedHorizontalEdge, moveLeft,
                    minimumWidth, maximumWidth, _monitorBounds.Left, _monitorBounds.Right);
            }
            if (resizeVertical && NearIntervals(proposedBounds.Left, proposedBounds.Right, peer.Left, peer.Right))
            {
                ConsiderResize(ref vertical, peer.Top, verticalEdge, fixedVerticalEdge, moveTop,
                    minimumHeight, maximumHeight, _monitorBounds.Top, _monitorBounds.Bottom);
                ConsiderResize(ref vertical, peer.Bottom, verticalEdge, fixedVerticalEdge, moveTop,
                    minimumHeight, maximumHeight, _monitorBounds.Top, _monitorBounds.Bottom);
            }
        }

        SetGuides(horizontal, vertical);
        return new WindowSnapRectangle(
            moveLeft && VerticalGuide is { IsSnapped: true } ? horizontal!.Value.Coordinate : proposedBounds.Left,
            moveTop && HorizontalGuide is { IsSnapped: true } ? vertical!.Value.Coordinate : proposedBounds.Top,
            !moveLeft && VerticalGuide is { IsSnapped: true } ? horizontal!.Value.Coordinate : proposedBounds.Right,
            !moveTop && HorizontalGuide is { IsSnapped: true } ? vertical!.Value.Coordinate : proposedBounds.Bottom);
    }

    private bool Prepare(WindowSnapRectangle proposedBounds, IReadOnlyList<WindowSnapRectangle> peerBounds)
    {
        ArgumentNullException.ThrowIfNull(peerBounds);
        Validate(proposedBounds, nameof(proposedBounds));
        foreach (var peer in peerBounds)
        {
            Validate(peer, nameof(peerBounds));
        }

        if ((long)proposedBounds.Left < (long)_monitorBounds.Left - _snapDistance
            || (long)proposedBounds.Right > (long)_monitorBounds.Right + _snapDistance
            || (long)proposedBounds.Top < (long)_monitorBounds.Top - _snapDistance
            || (long)proposedBounds.Bottom > (long)_monitorBounds.Bottom + _snapDistance)
        {
            // Permit near-edge overshoot so a sampled pointer move can still reach the monitor edge.
            // Deliberately moving farther outside releases snapping for the remainder of this drag.
            IsSuppressed = true;
        }

        if (IsSuppressed)
        {
            IsSnapped = false;
            VerticalGuide = null;
            HorizontalGuide = null;
            return false;
        }

        return true;
    }

    private void SetGuides(Alignment? horizontal, Alignment? vertical)
    {
        VerticalGuide = ToGuide(horizontal);
        HorizontalGuide = ToGuide(vertical);
        IsSnapped = VerticalGuide is { IsSnapped: true } || HorizontalGuide is { IsSnapped: true };
    }

    private readonly record struct Alignment(int Delta, int Coordinate);

    private WindowSnapGuide? ToGuide(Alignment? alignment) => alignment is { } value
        ? new WindowSnapGuide(value.Coordinate, Math.Abs(value.Delta) <= _snapDistance)
        : null;

    private void ConsiderEdges(ref Alignment? best, int first, int last, int targetFirst, int targetLast, int monitorFirst, int monitorLast)
    {
        Consider(ref best, targetFirst, first, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetLast, last, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetLast, first, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetFirst, last, first, last, monitorFirst, monitorLast);
    }

    private void ConsiderResize(ref Alignment? best, int target, int edge, int fixedEdge, bool moveFirst,
        int minimumExtent, int maximumExtent, int monitorFirst, int monitorLast)
    {
        var extent = moveFirst ? (long)fixedEdge - target : (long)target - fixedEdge;
        if (extent < minimumExtent || extent > maximumExtent) return;
        Consider(ref best, target, edge, edge, edge, monitorFirst, monitorLast);
    }

    private void Consider(ref Alignment? best, int target, int edge, int first, int last, int monitorFirst, int monitorLast)
    {
        var delta = (long)target - edge;
        if (Math.Abs(delta) > _guideDistance || first + delta < monitorFirst || last + delta > monitorLast)
        {
            return;
        }

        // Compare in widened arithmetic because valid desktop rectangles may straddle extreme signed coordinates.
        if (best is null || Math.Abs(delta) < Math.Abs(best.Value.Delta)
            || (Math.Abs(delta) == Math.Abs(best.Value.Delta) && delta < best.Value.Delta)
            || (delta == best.Value.Delta && target < best.Value.Coordinate))
        {
            best = new Alignment((int)delta, target);
        }
    }

    private bool NearIntervals(int first, int last, int peerFirst, int peerLast) =>
        (long)first <= (long)peerLast + _snapDistance && (long)peerFirst <= (long)last + _snapDistance;

    private static bool Contains(WindowSnapRectangle container, WindowSnapRectangle value) =>
        value.Left >= container.Left && value.Right <= container.Right
        && value.Top >= container.Top && value.Bottom <= container.Bottom;

    private static void Validate(WindowSnapRectangle bounds, string parameterName)
    {
        var width = (long)bounds.Right - bounds.Left;
        var height = (long)bounds.Bottom - bounds.Top;
        if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue)
        {
            throw new ArgumentException("Snap rectangles must have positive, representable physical pixel extents.", parameterName);
        }
    }
}
