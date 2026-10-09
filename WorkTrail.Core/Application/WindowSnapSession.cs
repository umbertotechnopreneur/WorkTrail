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

/// <summary>Calculates ten-pixel edge snapping; leaving the starting monitor by more than ten pixels suppresses the remaining drag.</summary>
public sealed class WindowSnapSession
{
    private const int SnapDistance = 10;
    private const int GuideDistance = 20;
    private readonly WindowSnapRectangle _monitorBounds;
    private readonly WindowSnapRectangle _workArea;

    /// <summary>Starts a drag on a physical monitor and its contained work area; invalid or unrepresentable rectangles fail.</summary>
    public WindowSnapSession(WindowSnapRectangle monitorBounds, WindowSnapRectangle workArea)
    {
        Validate(monitorBounds, nameof(monitorBounds));
        Validate(workArea, nameof(workArea));
        if (!Contains(monitorBounds, workArea))
        {
            throw new ArgumentException("The work area must be contained within the monitor bounds.", nameof(workArea));
        }

        _monitorBounds = monitorBounds;
        _workArea = workArea;
    }

    /// <summary>Gets whether a raw proposal exceeded the monitor's ten-pixel margin, disabling snap until a new session.</summary>
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
    /// Peers must overlap or lie within ten pixels on the perpendicular axis. Equal-distance targets
    /// resolve toward the smaller desktop coordinate, independently of peer order. Every input is validated,
    /// including after suppression; callers must pass unsnapped bounds to avoid making an edge sticky.
    /// </summary>
    public WindowSnapRectangle Move(WindowSnapRectangle proposedBounds, IReadOnlyList<WindowSnapRectangle> peerBounds)
    {
        ArgumentNullException.ThrowIfNull(peerBounds);
        Validate(proposedBounds, nameof(proposedBounds));
        foreach (var peer in peerBounds)
        {
            Validate(peer, nameof(peerBounds));
        }

        if ((long)proposedBounds.Left < (long)_monitorBounds.Left - SnapDistance
            || (long)proposedBounds.Right > (long)_monitorBounds.Right + SnapDistance
            || (long)proposedBounds.Top < (long)_monitorBounds.Top - SnapDistance
            || (long)proposedBounds.Bottom > (long)_monitorBounds.Bottom + SnapDistance)
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
            return proposedBounds;
        }

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

        VerticalGuide = ToGuide(horizontal);
        HorizontalGuide = ToGuide(vertical);
        var deltaX = VerticalGuide is { IsSnapped: true } ? horizontal!.Value.Delta : 0;
        var deltaY = HorizontalGuide is { IsSnapped: true } ? vertical!.Value.Delta : 0;
        IsSnapped = VerticalGuide is { IsSnapped: true } || HorizontalGuide is { IsSnapped: true };
        return new WindowSnapRectangle(
            checked(proposedBounds.Left + deltaX), checked(proposedBounds.Top + deltaY),
            checked(proposedBounds.Right + deltaX), checked(proposedBounds.Bottom + deltaY));
    }

    private readonly record struct Alignment(int Delta, int Coordinate);

    private static WindowSnapGuide? ToGuide(Alignment? alignment) => alignment is { } value
        ? new WindowSnapGuide(value.Coordinate, Math.Abs(value.Delta) <= SnapDistance)
        : null;

    private static void ConsiderEdges(ref Alignment? best, int first, int last, int targetFirst, int targetLast, int monitorFirst, int monitorLast)
    {
        Consider(ref best, targetFirst, first, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetLast, last, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetLast, first, first, last, monitorFirst, monitorLast);
        Consider(ref best, targetFirst, last, first, last, monitorFirst, monitorLast);
    }

    private static void Consider(ref Alignment? best, int target, int edge, int first, int last, int monitorFirst, int monitorLast)
    {
        var delta = (long)target - edge;
        if (Math.Abs(delta) > GuideDistance || first + delta < monitorFirst || last + delta > monitorLast)
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

    private static bool NearIntervals(int first, int last, int peerFirst, int peerLast) =>
        (long)first <= (long)peerLast + SnapDistance && (long)peerFirst <= (long)last + SnapDistance;

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
