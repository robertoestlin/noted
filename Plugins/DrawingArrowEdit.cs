using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted;

internal sealed partial class DrawingWindow
{
    private const double MinimumExitLeg = 32;
    private const double MinimumEntryLeg = 80;
    private const double ArrowSegmentHitRadius = 14;

    private int _activeArrowSegment = -1;

    private IReadOnlyList<Point> ResolveArrowPath(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
            return new[] { arrow.P1, arrow.P2 };

        if (arrow.RouteBends.Count > 0)
        {
            var fromBends = BuildPathFromRouteBends(arrow.P1, arrow.RouteBends, arrow.P2);
            if (fromBends.Length >= 2 && IsOrthogonalPath(fromBends))
                return SimplifyOrthogonalPath(fromBends);
        }

        DrawItem? startShape = arrow.AnchorStartShapeId is int sid ? FindShapeById(sid) : null;
        DrawItem? endShape = arrow.AnchorEndShapeId is int eid ? FindShapeById(eid) : null;
        return BuildOrthogonalArrowPath(
            arrow.P1, arrow.P2,
            startShape, arrow.AnchorStartIndex,
            endShape, arrow.AnchorEndIndex);
    }

    private static Point[] BuildPathFromRouteBends(Point start, List<Point> bends, Point end)
    {
        var path = new List<Point> { start };
        path.AddRange(bends);
        path.Add(end);
        return path.ToArray();
    }

    private void EnsureArrowRouteBends(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
            return;

        if (arrow.RouteBends.Count > 0)
        {
            CanonicalizeArrowEndpoints(arrow);
            if (arrow.RouteBends.Count > 0)
            {
                ApplyMinimumLegsToRoute(arrow);
                return;
            }
        }

        InitializeArrowRouteBends(arrow);
    }

    private void ApplyMinimumLegsToRoute(DrawItem arrow)
    {
        var path = BuildPathFromRouteBends(arrow.P1, arrow.RouteBends, arrow.P2);
        DrawItem? startShape = arrow.AnchorStartShapeId is int sid ? FindShapeById(sid) : null;
        DrawItem? endShape = arrow.AnchorEndShapeId is int eid ? FindShapeById(eid) : null;
        path = ExtendFirstExitLeg(path, startShape, arrow.AnchorStartIndex, arrow.P1);
        path = ExtendLastEntryLeg(path, endShape, arrow.AnchorEndIndex, arrow.P2);
        arrow.RouteBends = ExtractInteriorPoints(path);
    }

    private Point[] ResolveAutoOrthogonalPath(DrawItem arrow)
    {
        DrawItem? startShape = arrow.AnchorStartShapeId is int sid ? FindShapeById(sid) : null;
        DrawItem? endShape = arrow.AnchorEndShapeId is int eid ? FindShapeById(eid) : null;
        var path = BuildOrthogonalArrowPath(
            arrow.P1, arrow.P2,
            startShape, arrow.AnchorStartIndex,
            endShape, arrow.AnchorEndIndex);
        return path is Point[] arr ? arr : path.ToArray();
    }

    private static List<Point> ExtractInteriorPoints(IReadOnlyList<Point> path)
    {
        var bends = new List<Point>();
        for (var i = 1; i < path.Count - 1; i++)
            bends.Add(path[i]);
        return bends;
    }

    private void SyncArrowRouteBendsToAnchors(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct || arrow.RouteBends.Count == 0)
            return;

        DrawItem? startShape = arrow.AnchorStartShapeId is int sid ? FindShapeById(sid) : null;
        DrawItem? endShape = arrow.AnchorEndShapeId is int eid ? FindShapeById(eid) : null;

        var bends = arrow.RouteBends;
        var startOutward = GetAnchorOutwardUnit(startShape, arrow.AnchorStartIndex, arrow.P1);
        if (Math.Abs(startOutward.Y) > 0.5)
            bends[0] = new Point(arrow.P1.X, bends[0].Y);
        else if (Math.Abs(startOutward.X) > 0.5)
            bends[0] = new Point(bends[0].X, arrow.P1.Y);

        var endOutward = GetAnchorOutwardUnit(endShape, arrow.AnchorEndIndex, arrow.P2);
        var last = bends.Count - 1;
        if (Math.Abs(endOutward.Y) > 0.5)
            bends[last] = new Point(arrow.P2.X, bends[last].Y);
        else if (Math.Abs(endOutward.X) > 0.5)
            bends[last] = new Point(bends[last].X, arrow.P2.Y);
    }

    private static void WriteRouteBendsFromPath(DrawItem arrow, IReadOnlyList<Point> path)
    {
        arrow.RouteBends = ExtractInteriorPoints(path);
    }

    private static bool IsInteriorSegment(int segmentIndex, int pointCount)
        => segmentIndex > 0 && segmentIndex < pointCount - 2;

    private static bool SegmentIsHorizontal(Point a, Point b)
        => Math.Abs(a.Y - b.Y) < 0.001;

    private sealed record ArrowSegmentHandle(int SegmentIndex, Point Midpoint, bool IsHorizontal);

    private IReadOnlyList<ArrowSegmentHandle> GetArrowSegmentHandles(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
            return Array.Empty<ArrowSegmentHandle>();

        var path = ResolveArrowPath(arrow);
        if (path.Count < 3)
            return Array.Empty<ArrowSegmentHandle>();

        var handles = new List<ArrowSegmentHandle>();
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (!IsInteriorSegment(i, path.Count))
                continue;
            var a = path[i];
            var b = path[i + 1];
            if (!IsAxisAligned(a, b))
                continue;
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            handles.Add(new ArrowSegmentHandle(i, mid, SegmentIsHorizontal(a, b)));
        }
        return handles;
    }

    private int GetArrowSegmentHandleAt(DrawItem arrow, Point p)
    {
        var path = ResolveArrowPath(arrow);
        var best = -1;
        var bestDist = ArrowSegmentHitRadius;
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (!IsInteriorSegment(i, path.Count))
                continue;
            var a = path[i];
            var b = path[i + 1];
            if (!IsAxisAligned(a, b))
                continue;
            var dist = DistanceToSegment(p, a, b);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 0.0001)
            return (p - a).Length;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length;
    }

    private static Cursor CursorForArrowSegment(bool isHorizontal)
        => isHorizontal ? Cursors.SizeNS : Cursors.SizeWE;

    private void DragArrowSegment(DrawItem arrow, int segmentIndex, Point p)
    {
        EnsureArrowRouteBends(arrow);
        var path = BuildPathFromRouteBends(arrow.P1, arrow.RouteBends, arrow.P2).ToList();

        if (segmentIndex < 0 || segmentIndex >= path.Count - 1)
            return;

        var a = path[segmentIndex];
        var b = path[segmentIndex + 1];
        if (SegmentIsHorizontal(a, b))
        {
            var y = p.Y;
            for (var i = segmentIndex; i <= segmentIndex + 1; i++)
                path[i] = new Point(path[i].X, y);
        }
        else
        {
            var x = p.X;
            for (var i = segmentIndex; i <= segmentIndex + 1; i++)
                path[i] = new Point(x, path[i].Y);
        }

        WriteRouteBendsFromPath(arrow, path);
    }

    private bool TryBeginArrowSegmentDrag(DrawItem arrow, Point p)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
            return false;

        var seg = GetArrowSegmentHandleAt(arrow, p);
        if (seg < 0)
            return false;

        EnsureArrowRouteBends(arrow);
        _activeArrowSegment = seg;
        _activeHandle = -1;
        _isMoving = false;
        _moveLast = p;
        _pendingUndoForGesture = true;
        _canvas.CaptureMouse();
        return true;
    }

    private void RenderArrowSegmentHandles(DrawItem arrow)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        foreach (var handle in GetArrowSegmentHandles(arrow))
        {
            var hr = new System.Windows.Shapes.Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = accent,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(hr, handle.Midpoint.X - HandleSize / 2);
            Canvas.SetTop(hr, handle.Midpoint.Y - HandleSize / 2);
            _overlay.Children.Add(hr);
        }
    }

    private static Point[] ExtendFirstExitLeg(
        Point[] path,
        DrawItem? startShape,
        int? startIndex,
        Point start)
    {
        if (path.Length < 2 || startShape == null)
            return path;

        var outward = GetAnchorOutwardUnit(startShape, startIndex, start);
        if (Math.Abs(outward.X) < 0.5 && Math.Abs(outward.Y) < 0.5)
            return path;

        var leg = (path[1] - path[0]).Length;
        if (leg >= MinimumExitLeg)
            return path;

        var extended = path.ToArray();
        if (Math.Abs(outward.Y) > 0.5)
        {
            var y = start.Y + outward.Y * MinimumExitLeg;
            extended[1] = new Point(start.X, y);
            for (var i = 2; i < extended.Length; i++)
            {
                if (Math.Abs(extended[i].X - path[1].X) < 0.001)
                    extended[i] = new Point(extended[i].X, y);
            }
        }
        else
        {
            var x = start.X + outward.X * MinimumExitLeg;
            extended[1] = new Point(x, start.Y);
            for (var i = 2; i < extended.Length; i++)
            {
                if (Math.Abs(extended[i].Y - path[1].Y) < 0.001)
                    extended[i] = new Point(x, extended[i].Y);
            }
        }
        return SimplifyOrthogonalPath(extended) is Point[] arr ? arr : extended;
    }

    private static Point[] ExtendLastEntryLeg(
        Point[] path,
        DrawItem? endShape,
        int? endIndex,
        Point end)
    {
        if (path.Length < 2 || endShape == null)
            return path;

        var outward = GetAnchorOutwardUnit(endShape, endIndex, end);
        if (Math.Abs(outward.X) < 0.5 && Math.Abs(outward.Y) < 0.5)
            return path;

        var n = path.Length;
        var leg = (path[n - 1] - path[n - 2]).Length;
        if (leg >= MinimumEntryLeg)
            return path;

        var extended = path.ToArray();
        var penultimate = n - 2;
        var corridorCoord = path[penultimate];

        if (Math.Abs(outward.Y) > 0.5)
        {
            var approachY = end.Y + outward.Y * MinimumEntryLeg;
            extended[penultimate] = new Point(end.X, approachY);
            for (var i = 1; i < penultimate; i++)
            {
                if (Math.Abs(extended[i].Y - corridorCoord.Y) < 0.001)
                    extended[i] = new Point(extended[i].X, approachY);
            }
        }
        else
        {
            var approachX = end.X + outward.X * MinimumEntryLeg;
            extended[penultimate] = new Point(approachX, end.Y);
            for (var i = 1; i < penultimate; i++)
            {
                if (Math.Abs(extended[i].X - corridorCoord.X) < 0.001)
                    extended[i] = new Point(approachX, extended[i].Y);
            }
        }

        return SimplifyOrthogonalPath(extended) is Point[] arr ? arr : extended;
    }

    /// <summary>
    /// Orthogonal routing is direction-independent: P1 is always the upper/left anchor, P2 the lower/right.
    /// </summary>
    private static bool ShouldSwapToCanonicalArrowOrder(DrawItem arrow)
    {
        const double eps = 0.5;
        if (arrow.P1.Y < arrow.P2.Y - eps)
            return false;
        if (arrow.P1.Y > arrow.P2.Y + eps)
            return true;
        if (arrow.P1.X < arrow.P2.X - eps)
            return false;
        if (arrow.P1.X > arrow.P2.X + eps)
            return true;
        return false;
    }

    private static void SwapArrowEndpoints(DrawItem arrow)
    {
        (arrow.P1, arrow.P2) = (arrow.P2, arrow.P1);
        (arrow.AnchorStartShapeId, arrow.AnchorEndShapeId) = (arrow.AnchorEndShapeId, arrow.AnchorStartShapeId);
        (arrow.AnchorStartIndex, arrow.AnchorEndIndex) = (arrow.AnchorEndIndex, arrow.AnchorStartIndex);
        arrow.RouteBends.Clear();
    }

    private static void CanonicalizeArrowEndpoints(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
            return;
        if (!ShouldSwapToCanonicalArrowOrder(arrow))
            return;
        SwapArrowEndpoints(arrow);
    }

    /// <summary>Simple arrow points at the draw-release end (second object).</summary>
    private static void ApplySimpleHeadTargetFromDrawOrder(DrawItem arrow)
    {
        if (arrow.Kind != "arrow")
            return;
        if (arrow.Direct)
        {
            // Direct lines never swap endpoints; P2 is always where the user released.
            arrow.SimpleHeadAtP2 = true;
            return;
        }
        // Orthogonal paths may swap P1/P2 for routing — head stays on the original release end.
        arrow.SimpleHeadAtP2 = !ShouldSwapToCanonicalArrowOrder(arrow);
    }

    private void InitializeArrowRouteBends(DrawItem arrow)
    {
        if (arrow.Kind != "arrow" || arrow.Direct)
        {
            arrow.RouteBends.Clear();
            return;
        }

        CanonicalizeArrowEndpoints(arrow);
        var path = ResolveAutoOrthogonalPath(arrow);
        DrawItem? startShape = arrow.AnchorStartShapeId is int sid ? FindShapeById(sid) : null;
        DrawItem? endShape = arrow.AnchorEndShapeId is int eid ? FindShapeById(eid) : null;
        path = ExtendFirstExitLeg(path, startShape, arrow.AnchorStartIndex, arrow.P1);
        path = ExtendLastEntryLeg(path, endShape, arrow.AnchorEndIndex, arrow.P2);
        arrow.RouteBends = ExtractInteriorPoints(path);
    }
}
