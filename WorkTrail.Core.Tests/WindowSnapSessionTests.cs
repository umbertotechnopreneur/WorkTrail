// SPDX-License-Identifier: MIT

using System;
using WorkTrail.Application;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class WindowSnapSessionTests
{
    private static readonly WindowSnapRectangle Monitor = new(0, 0, 1000, 1000);

    /// <summary>All eight resize directions move only their dragged edges and show only their applicable guides.</summary>
    [Theory]
    [InlineData(WindowSnapEdges.Left, 400, 405, 595, 595)]
    [InlineData(WindowSnapEdges.Right, 405, 405, 600, 595)]
    [InlineData(WindowSnapEdges.Top, 405, 400, 595, 595)]
    [InlineData(WindowSnapEdges.Bottom, 405, 405, 595, 600)]
    [InlineData(WindowSnapEdges.Top | WindowSnapEdges.Left, 400, 400, 595, 595)]
    [InlineData(WindowSnapEdges.Top | WindowSnapEdges.Right, 405, 400, 600, 595)]
    [InlineData(WindowSnapEdges.Bottom | WindowSnapEdges.Left, 400, 405, 595, 600)]
    [InlineData(WindowSnapEdges.Bottom | WindowSnapEdges.Right, 405, 405, 600, 600)]
    public void Resize_SnapsOnlyDraggedEdges(WindowSnapEdges edges, int left, int top, int right, int bottom)
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var result = session.Resize(new(405, 405, 595, 595), [new(400, 400, 600, 600)], edges);
        Assert.Equal(new WindowSnapRectangle(left, top, right, bottom), result);
        Assert.True(session.IsSnapped);
        Assert.Equal((edges & (WindowSnapEdges.Left | WindowSnapEdges.Right)) != 0, session.VerticalGuide.HasValue);
        Assert.Equal((edges & (WindowSnapEdges.Top | WindowSnapEdges.Bottom)) != 0, session.HorizontalGuide.HasValue);
    }

    /// <summary>Snap and preview distances scale with the display, including fractional scaling rounded up to physical pixels.</summary>
    [Theory]
    [InlineData(96, 10, 20)]
    [InlineData(120, 13, 25)]
    [InlineData(144, 15, 30)]
    [InlineData(192, 20, 40)]
    [InlineData(288, 30, 60)]
    public void Dpi_ScalesMovementAndResizeThresholds(int dpi, int snapDistance, int guideDistance)
    {
        var session = new WindowSnapSession(Monitor, Monitor, (uint)dpi);
        Assert.Equal(0, session.Move(new(snapDistance, 200, snapDistance + 100, 300), []).Left);
        Assert.Equal(new WindowSnapGuide(0, true), session.VerticalGuide);
        var preview = new WindowSnapRectangle(guideDistance, 200, guideDistance + 100, 300);
        Assert.Equal(preview, session.Move(preview, []));
        Assert.Equal(new WindowSnapGuide(0, false), session.VerticalGuide);
        session.Move(new(guideDistance + 1, 200, guideDistance + 101, 300), []);
        Assert.Null(session.VerticalGuide);

        var raw = new WindowSnapRectangle(200, 200, 1000 - snapDistance, 400);
        var resized = session.Resize(raw, [], WindowSnapEdges.Right);
        Assert.Equal(raw with { Right = 1000 }, resized);
        Assert.Equal(new WindowSnapGuide(1000, true), session.VerticalGuide);
        raw = raw with { Right = 1000 - guideDistance };
        Assert.Equal(raw, session.Resize(raw, [], WindowSnapEdges.Right));
        Assert.Equal(new WindowSnapGuide(1000, false), session.VerticalGuide);
        raw = raw with { Right = raw.Right - 1 };
        Assert.Equal(raw, session.Resize(raw, [], WindowSnapEdges.Right));
        Assert.Null(session.VerticalGuide);
    }

    /// <summary>Impossible snap targets are rejected before nearest-edge selection, so a legal alternative can win.</summary>
    [Fact]
    public void Resize_RespectsMinimumAndMaximumSize()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var raw = new WindowSnapRectangle(405, 300, 605, 600);
        Assert.Equal(raw, session.Resize(raw, [new(410, 200, 800, 700)], WindowSnapEdges.Left, minimumWidth: 200));
        Assert.Null(session.VerticalGuide);
        Assert.Equal(raw, session.Resize(raw, [new(400, 200, 800, 700)], WindowSnapEdges.Left, maximumWidth: 200));
        Assert.Null(session.VerticalGuide);
        Assert.Equal(raw with { Left = 400 }, session.Resize(raw,
            [new(410, 200, 800, 700), new(400, 200, 800, 700)], WindowSnapEdges.Left, minimumWidth: 200));
        Assert.Equal(new WindowSnapGuide(400, true), session.VerticalGuide);
    }

    /// <summary>Preview and release use raw dimensions; a formerly snapped size does not keep the edge sticky.</summary>
    [Fact]
    public void Resize_ReleasesEdgesAndClearsGuidesAfterEscape()
    {
        var session = new WindowSnapSession(Monitor, Monitor, 144);
        var raw = new WindowSnapRectangle(200, 200, 985, 400);
        Assert.Equal(1000, session.Resize(raw, [], WindowSnapEdges.Right).Right);
        raw = raw with { Right = 984 };
        Assert.Equal(raw, session.Resize(raw, [], WindowSnapEdges.Right));
        Assert.Equal(new WindowSnapGuide(1000, false), session.VerticalGuide);
        raw = raw with { Right = 1016 };
        Assert.Equal(raw, session.Resize(raw, [], WindowSnapEdges.Right));
        Assert.True(session.IsSuppressed);
        Assert.Null(session.VerticalGuide);
        session.UpdateDpi(192);
        raw = raw with { Right = 985 };
        Assert.Equal(raw, session.Resize(raw, [], WindowSnapEdges.Right));
        Assert.Null(session.VerticalGuide);
    }

    /// <summary>A DPI change updates an active operation's thresholds instead of retaining the old monitor scale.</summary>
    [Fact]
    public void Dpi_RefreshesAnActiveOperation()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var raw = new WindowSnapRectangle(15, 200, 115, 400);
        Assert.Equal(raw, session.Move(raw, []));
        session.UpdateDpi(144);
        Assert.Equal(0, session.Move(raw, []).Left);
        session.UpdateDpi(96);
        Assert.Equal(raw, session.Move(raw, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.UpdateDpi(0));
    }

    /// <summary>Resizing retains signed monitor coordinates and deterministically resolves equal-distance peers.</summary>
    [Fact]
    public void Resize_SupportsNegativeCoordinatesAndPeerOrder()
    {
        var monitor = new WindowSnapRectangle(-1000, -1000, 0, 0);
        var session = new WindowSnapSession(monitor, monitor, 144);
        var raw = new WindowSnapRectangle(-700, -700, -400, -400);
        WindowSnapRectangle[] peers = [new(-605, -800, -405, -200), new(-595, -800, -395, -200)];
        var result = session.Resize(raw, peers, WindowSnapEdges.Right);
        Assert.Equal(raw with { Right = -405 }, result);
        Assert.Equal(new WindowSnapGuide(-405, true), session.VerticalGuide);
        Array.Reverse(peers);
        Assert.Equal(result, session.Resize(raw, peers, WindowSnapEdges.Right));
    }

    /// <summary>Invalid directions and size constraints cannot generate malformed resize rectangles.</summary>
    [Fact]
    public void Resize_RejectsInvalidDirectionsAndConstraints()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var raw = new WindowSnapRectangle(200, 200, 400, 400);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Left | WindowSnapEdges.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Top | WindowSnapEdges.Bottom));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], (WindowSnapEdges)16));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Right, minimumWidth: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Right, minimumHeight: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Right, minimumWidth: 200, maximumWidth: 199));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resize(raw, [], WindowSnapEdges.Right, minimumHeight: 200, maximumHeight: 199));
        Assert.Throws<ArgumentNullException>(() => session.Resize(raw, null!, WindowSnapEdges.Right));
    }

    /// <summary>Preview guides do not move the window; only the existing ten-pixel threshold enables snapping.</summary>
    [Theory]
    [InlineData(21, null, false, 21)]
    [InlineData(20, 0, false, 20)]
    [InlineData(11, 0, false, 11)]
    [InlineData(10, 0, true, 0)]
    [InlineData(0, 0, true, 0)]
    public void Guides_PreviewWithoutChangingTheSnapThreshold(int left, int? coordinate, bool snapped, int expectedLeft)
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var result = session.Move(new(left, 200, left + 100, 300), []);
        Assert.Equal(expectedLeft, result.Left);
        Assert.Equal(coordinate.HasValue ? new WindowSnapGuide(coordinate.Value, snapped) : (WindowSnapGuide?)null,
            session.VerticalGuide);
        Assert.Null(session.HorizontalGuide);
        Assert.Equal(snapped, session.IsSnapped);
    }

    /// <summary>Both guide orientations refer to the same peer edges selected for placement, including exact matches.</summary>
    [Fact]
    public void Guides_MatchSelectedPeerEdgesAndClearAfterEscape()
    {
        var monitor = new WindowSnapRectangle(-1000, -1000, 0, 0);
        var session = new WindowSnapSession(monitor, monitor);
        WindowSnapRectangle[] peers = [new(-600, -600, -400, -400)];
        var result = session.Move(new(-605, -415, -505, -315), peers);
        Assert.Equal(new WindowSnapRectangle(-600, -415, -500, -315), result);
        Assert.Equal(new WindowSnapGuide(-600, true), session.VerticalGuide);
        Assert.Equal(new WindowSnapGuide(-400, false), session.HorizontalGuide);
        result = session.Move(new(-600, -400, -500, -300), peers);
        Assert.Equal(new WindowSnapRectangle(-600, -400, -500, -300), result);
        Assert.Equal(new WindowSnapGuide(-600, true), session.VerticalGuide);
        Assert.Equal(new WindowSnapGuide(-400, true), session.HorizontalGuide);
        session.Move(new(-1011, -400, -911, -300), peers);
        Assert.True(session.IsSuppressed);
        Assert.Null(session.VerticalGuide);
        Assert.Null(session.HorizontalGuide);
        session.Move(new(-600, -400, -500, -300), peers);
        Assert.Null(session.VerticalGuide);
        Assert.Null(session.HorizontalGuide);
    }

    /// <summary>Equidistant references produce stable placement and guides regardless of peer enumeration order.</summary>
    [Fact]
    public void Guides_SelectTheSameTargetRegardlessOfPeerOrder()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var proposed = new WindowSnapRectangle(300, 200, 400, 300);
        WindowSnapRectangle[] peers = [new(295, 150, 495, 350), new(305, 150, 505, 350)];
        var result = session.Move(proposed, peers);
        var guide = session.VerticalGuide;
        Assert.Equal(295, result.Left);
        Assert.Equal(new WindowSnapGuide(295, true), guide);
        Array.Reverse(peers);
        Assert.Equal(result, session.Move(proposed, peers));
        Assert.Equal(guide, session.VerticalGuide);
        session.Move(new(100, 500, 200, 600), peers);
        Assert.Null(session.VerticalGuide);
        Assert.Null(session.HorizontalGuide);
    }

    /// <summary>All four edges snap within ten physical pixels, including outward overshoot, without resizing.</summary>
    [Theory]
    [InlineData(5, 200, 105, 300, 0, 200, 100, 300)]
    [InlineData(895, 200, 995, 300, 900, 200, 1000, 300)]
    [InlineData(200, 5, 300, 105, 200, 0, 300, 100)]
    [InlineData(200, 895, 300, 995, 200, 900, 300, 1000)]
    [InlineData(10, 200, 110, 300, 0, 200, 100, 300)]
    [InlineData(890, 200, 990, 300, 900, 200, 1000, 300)]
    [InlineData(200, 10, 300, 110, 200, 0, 300, 100)]
    [InlineData(200, 890, 300, 990, 200, 900, 300, 1000)]
    [InlineData(-10, 200, 90, 300, 0, 200, 100, 300)]
    [InlineData(910, 200, 1010, 300, 900, 200, 1000, 300)]
    [InlineData(200, -10, 300, 90, 200, 0, 300, 100)]
    [InlineData(200, 910, 300, 1010, 200, 900, 300, 1000)]
    [InlineData(-10, -10, 90, 90, 0, 0, 100, 100)]
    public void Move_SnapsAllWorkAreaEdgesWithinTenPixels(int left, int top, int right, int bottom,
        int expectedLeft, int expectedTop, int expectedRight, int expectedBottom)
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var proposed = new WindowSnapRectangle(left, top, right, bottom);
        var result = session.Move(proposed, []);
        Assert.Equal(new WindowSnapRectangle(expectedLeft, expectedTop, expectedRight, expectedBottom), result);
        Assert.Equal(proposed.Width, result.Width);
        Assert.Equal(proposed.Height, result.Height);
        Assert.False(session.IsSuppressed);
    }

    /// <summary>Eleven pixels is outside the inclusive threshold on every edge.</summary>
    [Theory]
    [InlineData(11, 200, 111, 300)]
    [InlineData(889, 200, 989, 300)]
    [InlineData(200, 11, 300, 111)]
    [InlineData(200, 889, 300, 989)]
    public void Move_DoesNotSnapAtElevenPixels(int left, int top, int right, int bottom)
    {
        var proposed = new WindowSnapRectangle(left, top, right, bottom);
        Assert.Equal(proposed, new WindowSnapSession(Monitor, Monitor).Move(proposed, []));
    }

    /// <summary>Each move uses its raw bounds and releases a snapped edge as soon as the threshold is exceeded.</summary>
    [Fact]
    public void Move_DoesNotAccumulatePreviousSnapOffset()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.Equal(0, session.Move(new(5, 200, 105, 300), []).Left);
        Assert.Equal(11, session.Move(new(11, 200, 111, 300), []).Left);
        Assert.False(session.IsSuppressed);
    }

    /// <summary>Exact edge matches count as snapped even without translation, and moving back into the interior clears that state.</summary>
    [Fact]
    public void IsSnapped_TracksExactEdgesAndResetsForInteriorMoves()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.False(session.IsSnapped);
        var exactEdge = new WindowSnapRectangle(0, 200, 100, 300);
        Assert.Equal(exactEdge, session.Move(exactEdge, []));
        Assert.True(session.IsSnapped);
        var interior = new WindowSnapRectangle(200, 200, 300, 300);
        Assert.Equal(interior, session.Move(interior, []));
        Assert.False(session.IsSnapped);
        Assert.Equal(interior, session.Move(interior, [new(300, 200, 400, 300)]));
        Assert.True(session.IsSnapped);
        session.Move(new(-11, 200, 89, 300), []);
        Assert.True(session.IsSuppressed);
        Assert.False(session.IsSnapped);
        session.Move(exactEdge, []);
        Assert.False(session.IsSnapped);
    }

    /// <summary>Monitors above or left of the primary display retain their signed desktop coordinates.</summary>
    [Fact]
    public void Move_SupportsNegativeMonitorCoordinates()
    {
        var monitor = new WindowSnapRectangle(-1920, -1080, 0, 0);
        var session = new WindowSnapSession(monitor, monitor);
        Assert.Equal(new WindowSnapRectangle(-1920, -1080, -1720, -880), session.Move(new(-1915, -1075, -1715, -875), []));
        Assert.Equal(new WindowSnapRectangle(-200, -200, 0, 0), session.Move(new(-205, -205, -5, -5), []));
    }

    /// <summary>Peers support adjacency on all sides and matching corresponding left/right/top/bottom edges.</summary>
    [Theory]
    [InlineData(195, 430, 395, 530, 200, 430, 400, 530)]
    [InlineData(605, 430, 805, 530, 600, 430, 800, 530)]
    [InlineData(430, 195, 530, 395, 430, 200, 530, 400)]
    [InlineData(430, 605, 530, 805, 430, 600, 530, 800)]
    [InlineData(405, 450, 505, 550, 400, 450, 500, 550)]
    [InlineData(495, 450, 595, 550, 500, 450, 600, 550)]
    [InlineData(450, 405, 550, 505, 450, 400, 550, 500)]
    [InlineData(450, 495, 550, 595, 450, 500, 550, 600)]
    public void Move_SnapsAdjacentAndAlignedPeerEdges(int left, int top, int right, int bottom,
        int expectedLeft, int expectedTop, int expectedRight, int expectedBottom)
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.Equal(new WindowSnapRectangle(expectedLeft, expectedTop, expectedRight, expectedBottom),
            session.Move(new(left, top, right, bottom), [new(400, 400, 600, 600)]));
    }

    /// <summary>A matching edge on a distant window is not an eligible target.</summary>
    [Fact]
    public void Move_RejectsPeersDistantOnPerpendicularAxis()
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var proposed = new WindowSnapRectangle(195, 100, 395, 200);
        Assert.Equal(proposed, session.Move(proposed, [new(400, 400, 600, 600)]));
        proposed = new WindowSnapRectangle(100, 195, 200, 395);
        Assert.Equal(proposed, session.Move(proposed, [new(400, 400, 600, 600)]));
    }

    /// <summary>Corner proximity is accepted at ten pixels but rejected beyond it.</summary>
    [Fact]
    public void Move_BoundsPerpendicularProximityToTenPixels()
    {
        var proposed = new WindowSnapRectangle(200, 200, 300, 300);
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.Equal(new WindowSnapRectangle(210, 210, 310, 310), session.Move(proposed, [new(310, 310, 410, 410)]));
        Assert.Equal(proposed, session.Move(proposed, [new(310, 311, 410, 411)]));
    }

    /// <summary>The closest eligible edge wins; equal distances are deterministic regardless of peer enumeration order.</summary>
    [Fact]
    public void Move_ChoosesSmallestDeltaWithStableTies()
    {
        var proposed = new WindowSnapRectangle(400, 400, 600, 600);
        var left = new WindowSnapRectangle(397, 350, 697, 650);
        var right = new WindowSnapRectangle(403, 350, 703, 650);
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.Equal(397, session.Move(proposed, [left, right]).Left);
        Assert.Equal(397, session.Move(proposed, [right, left]).Left);
        Assert.Equal(402, session.Move(proposed, [left, new(402, 350, 702, 650)]).Left);
    }

    /// <summary>Touching the work area is not leaving the monitor; taskbar space neither suppresses nor forcibly clamps a move.</summary>
    [Fact]
    public void Move_UsesWorkAreaForTargetsAndMonitorForSuppression()
    {
        var session = new WindowSnapSession(Monitor, new(40, 20, 1000, 960));
        Assert.Equal(new WindowSnapRectangle(40, 200, 140, 300), session.Move(new(35, 200, 135, 300), []));
        Assert.Equal(new WindowSnapRectangle(200, 860, 300, 960), session.Move(new(200, 865, 300, 965), []));
        var taskbarBounds = new WindowSnapRectangle(200, 970, 300, 990);
        Assert.Equal(taskbarBounds, session.Move(taskbarBounds, []));
        Assert.False(session.IsSuppressed);
    }

    /// <summary>Exceeding any monitor edge by eleven pixels disables snapping for the rest of the drag, including re-entry.</summary>
    [Theory]
    [InlineData(-11, 100, 89, 200)]
    [InlineData(911, 100, 1011, 200)]
    [InlineData(100, -11, 200, 89)]
    [InlineData(100, 911, 200, 1011)]
    public void Move_SuppressesAfterEscapeUntilNewSession(int left, int top, int right, int bottom)
    {
        var session = new WindowSnapSession(Monitor, Monitor);
        var outside = new WindowSnapRectangle(left, top, right, bottom);
        Assert.Equal(outside, session.Move(outside, []));
        Assert.True(session.IsSuppressed);
        var reentry = new WindowSnapRectangle(5, 200, 105, 300);
        Assert.Equal(reentry, session.Move(reentry, [new(106, 200, 206, 300)]));
        var nextDrag = new WindowSnapSession(Monitor, Monitor);
        Assert.Equal(0, nextDrag.Move(reentry, []).Left);
        Assert.False(nextDrag.IsSuppressed);
    }

    /// <summary>A partially off-monitor peer cannot pull a valid window beyond the starting monitor.</summary>
    [Fact]
    public void Move_RejectsOutOfMonitorSnapTargets()
    {
        var session = new WindowSnapSession(Monitor, new(20, 20, 980, 980));
        var proposed = new WindowSnapRectangle(1, 200, 101, 300);
        Assert.Equal(proposed, session.Move(proposed, [new(-2, 150, 198, 350)]));
        Assert.False(session.IsSuppressed);
        // An inside edge of an otherwise off-monitor peer remains a usable alignment target.
        Assert.Equal(new WindowSnapRectangle(3, 200, 103, 300), session.Move(proposed, [new(-197, 150, 103, 350)]));
    }

    /// <summary>Valid extreme signed coordinates do not overflow delta or dimension arithmetic.</summary>
    [Fact]
    public void Move_WidensArithmeticBeforeComparingEdges()
    {
        var monitor = new WindowSnapRectangle(int.MinValue, -100, int.MinValue + 1000, 900);
        var proposed = new WindowSnapRectangle(int.MinValue + 5, 200, int.MinValue + 105, 300);
        var result = new WindowSnapSession(monitor, monitor).Move(proposed, [new(int.MaxValue - 100, 200, int.MaxValue, 300)]);
        Assert.Equal(int.MinValue, result.Left);
        Assert.Equal(100, result.Width);
    }

    /// <summary>Invalid monitor/work rectangles, malformed peers and null lists fail explicitly, even after suppression.</summary>
    [Fact]
    public void Inputs_RejectInvalidRectanglesAndNullPeers()
    {
        Assert.Throws<ArgumentException>(() => new WindowSnapSession(default, Monitor));
        Assert.Throws<ArgumentException>(() => new WindowSnapSession(Monitor, new(100, 100, 100, 200)));
        Assert.Throws<ArgumentException>(() => new WindowSnapSession(Monitor, new(-1, 0, 1000, 1000)));
        Assert.Throws<ArgumentException>(() => new WindowSnapSession(new(int.MinValue, 0, int.MaxValue, 100), Monitor));
        var session = new WindowSnapSession(Monitor, Monitor);
        Assert.Throws<ArgumentNullException>(() => session.Move(new(100, 100, 200, 200), null!));
        Assert.Throws<ArgumentException>(() => session.Move(new(200, 100, 100, 200), []));
        Assert.Throws<ArgumentException>(() => session.Move(new(100, 100, 200, 200), [new(0, 1, 10, 0)]));
        Assert.Throws<ArgumentException>(() => session.Move(new(int.MinValue, 0, int.MaxValue, 100), []));
        Assert.Throws<ArgumentException>(() => session.Move(new(100, 100, 200, 200), [new(0, int.MinValue, 100, int.MaxValue)]));
        Assert.False(session.IsSuppressed);
        session.Move(new(-11, 100, 89, 200), []);
        Assert.Throws<ArgumentException>(() => session.Move(default, []));
        Assert.Throws<ArgumentException>(() => session.Move(new(100, 100, 200, 200), [default]));
    }
}
