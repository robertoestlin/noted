using System.Windows;

namespace Noted;

internal sealed partial class DrawingWindow
{
    private const double ShapeClearanceMargin = 8;

    private IReadOnlyList<Point> GetArrowPathPoints(DrawItem arrow)
        => ResolveArrowPath(arrow);

    private static Point[] BuildOrthogonalArrowPath(
        Point start, Point end,
        DrawItem? startShape, int? startIndex,
        DrawItem? endShape, int? endIndex)
    {
        if (PointsNearlyEqual(start, end))
            return new[] { start, end };

        var startOutward = GetAnchorOutwardUnit(startShape, startIndex, start);
        var endOutward = GetAnchorOutwardUnit(endShape, endIndex, end);
        var exitVertical = Math.Abs(startOutward.Y) > 0.5;
        var entryVertical = Math.Abs(endOutward.Y) > 0.5;

        var candidates = new List<Point[]>();
        AddOrthogonalCandidates(candidates, start, end, startShape, endShape, startOutward, endOutward);

        Point[]? best = null;
        var bestScore = double.MaxValue;
        foreach (var path in candidates)
        {
            if (!IsOrthogonalPath(path))
                continue;

            var score = PathPolylineLength(path);
            if (PathCrossesAnchoredShapes(path, startShape, endShape))
                score += 100_000;
            if (!FirstSegmentExitsOutward(path, startOutward))
                score += 1000;
            if (!LastSegmentApproachesFromOutside(path, endOutward))
                score += 1000;
            if (!FirstSegmentIsVertical(path, exitVertical))
                score += 100;
            if (!LastSegmentIsVertical(path, entryVertical))
                score += 100;
            if (score < bestScore)
            {
                bestScore = score;
                best = path;
            }
        }

        if (best != null)
            return SimplifyOrthogonalPath(best) is Point[] simplified ? simplified : best;

        if (startShape != null)
        {
            var exit = GetClearancePoint(startShape, start, startOutward);
            var fallback = exitVertical
                ? new[] { start, exit, new Point(exit.X, end.Y), end }
                : new[] { start, exit, new Point(end.X, exit.Y), end };
            return SimplifyOrthogonalPath(fallback) is Point[] fb ? fb : fallback;
        }

        var simple = exitVertical
            ? new[] { start, new Point(start.X, end.Y), end }
            : new[] { start, new Point(end.X, start.Y), end };
        return SimplifyOrthogonalPath(simple) is Point[] sp ? sp : simple;
    }

    private static void AddOrthogonalCandidates(
        List<Point[]> candidates,
        Point start, Point end,
        DrawItem? startShape, DrawItem? endShape,
        Point startOutward, Point endOutward)
    {
        candidates.Add(new[] { start, new Point(end.X, start.Y), end });
        candidates.Add(new[] { start, new Point(start.X, end.Y), end });

        var midX = (start.X + end.X) / 2;
        var midY = (start.Y + end.Y) / 2;
        candidates.Add(new[] { start, new Point(start.X, midY), new Point(end.X, midY), end });
        candidates.Add(new[] { start, new Point(midX, start.Y), new Point(midX, end.Y), end });

        if (startShape != null)
        {
            var exit = GetClearancePoint(startShape, start, startOutward);
            candidates.Add(new[] { start, exit, new Point(end.X, exit.Y), end });
            candidates.Add(new[] { start, exit, new Point(exit.X, end.Y), end });
            candidates.Add(new[] { start, exit, new Point(exit.X, midY), new Point(end.X, midY), end });
            candidates.Add(new[] { start, exit, new Point(midX, exit.Y), new Point(midX, end.Y), end });
        }

        if (endShape != null)
        {
            var entry = GetClearancePoint(endShape, end, endOutward);
            candidates.Add(new[] { start, new Point(start.X, entry.Y), entry, end });
            candidates.Add(new[] { start, new Point(entry.X, start.Y), entry, end });
            candidates.Add(new[] { start, new Point(midX, start.Y), new Point(midX, entry.Y), entry, end });
            candidates.Add(new[] { start, new Point(start.X, midY), new Point(entry.X, midY), entry, end });
        }

        if (startShape != null && endShape != null)
        {
            var exit = GetClearancePoint(startShape, start, startOutward);
            var entry = GetClearancePoint(endShape, end, endOutward);
            candidates.Add(new[] { start, exit, entry, end });
            candidates.Add(new[] { start, exit, new Point(entry.X, exit.Y), entry, end });
            candidates.Add(new[] { start, exit, new Point(exit.X, entry.Y), entry, end });
            candidates.Add(new[] { start, exit, new Point(exit.X, midY), new Point(entry.X, midY), entry, end });
            candidates.Add(new[] { start, exit, new Point(midX, exit.Y), new Point(midX, entry.Y), entry, end });
        }
    }

    /// <summary>Unit vector along the axis pointing outward from the shape at this anchor.</summary>
    private static Point GetAnchorOutwardUnit(DrawItem? shape, int? index, Point anchor)
    {
        if (shape == null)
            return new Point(0, 0);

        if (shape.Kind == "rect" && index is int idx)
        {
            return idx switch
            {
                1 => new Point(0, -1),
                5 => new Point(0, 1),
                3 => new Point(1, 0),
                7 => new Point(-1, 0),
                _ => OutwardFromCenter(shape, anchor),
            };
        }

        if (shape.Kind == "ellipse" && index is int eidx)
        {
            return eidx switch
            {
                2 => new Point(0, -1),
                6 => new Point(0, 1),
                0 => new Point(1, 0),
                4 => new Point(-1, 0),
                _ => OutwardFromCenter(shape, anchor),
            };
        }

        return OutwardFromCenter(shape, anchor);
    }

    private static Point OutwardFromCenter(DrawItem shape, Point anchor)
    {
        var center = GetShapeCenter(shape);
        var dx = anchor.X - center.X;
        var dy = anchor.Y - center.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return new Point(0, -1);
        if (Math.Abs(dx) >= Math.Abs(dy))
            return new Point(dx > 0 ? 1 : -1, 0);
        return new Point(0, dy > 0 ? 1 : -1);
    }

    private static Point GetClearancePoint(DrawItem shape, Point anchor, Point outward)
    {
        var b = GetBounds(shape);
        if (outward.Y < 0)
            return new Point(anchor.X, b.Top - ShapeClearanceMargin);
        if (outward.Y > 0)
            return new Point(anchor.X, b.Bottom + ShapeClearanceMargin);
        if (outward.X < 0)
            return new Point(b.Left - ShapeClearanceMargin, anchor.Y);
        if (outward.X > 0)
            return new Point(b.Right + ShapeClearanceMargin, anchor.Y);
        return anchor;
    }

    private static bool FirstSegmentExitsOutward(Point[] path, Point outward)
    {
        if (path.Length < 2 || (Math.Abs(outward.X) < 0.5 && Math.Abs(outward.Y) < 0.5))
            return true;
        return SegmentFollowsAxisDirection(path[0], path[1], outward);
    }

    private static bool LastSegmentApproachesFromOutside(Point[] path, Point endOutward)
    {
        if (path.Length < 2 || (Math.Abs(endOutward.X) < 0.5 && Math.Abs(endOutward.Y) < 0.5))
            return true;
        var n = path.Length;
        return SegmentFollowsAxisDirection(path[n - 2], path[n - 1], new Point(-endOutward.X, -endOutward.Y));
    }

    private static bool SegmentFollowsAxisDirection(Point from, Point to, Point axis)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(axis.X) > 0.5)
            return Math.Abs(dx) > 0.001 && Math.Sign(dx) == Math.Sign(axis.X);
        if (Math.Abs(axis.Y) > 0.5)
            return Math.Abs(dy) > 0.001 && Math.Sign(dy) == Math.Sign(axis.Y);
        return true;
    }

    private static bool PathCrossesAnchoredShapes(Point[] path, DrawItem? startShape, DrawItem? endShape)
    {
        for (var i = 1; i < path.Length; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            if (startShape != null && SegmentCrossesShapeInterior(startShape, a, b))
                return true;
            if (endShape != null && !ReferenceEquals(endShape, startShape)
                && SegmentCrossesShapeInterior(endShape, a, b))
                return true;
        }
        return false;
    }

    private static bool SegmentCrossesShapeInterior(DrawItem shape, Point a, Point b)
    {
        var r = GetBounds(shape);
        if (r.Width < 1 || r.Height < 1)
            return false;

        var left = r.Left + 0.5;
        var top = r.Top + 0.5;
        var right = r.Right - 0.5;
        var bottom = r.Bottom - 0.5;

        if (Math.Abs(a.X - b.X) < 0.001)
        {
            var x = a.X;
            if (x <= left || x >= right)
                return false;
            var y0 = Math.Min(a.Y, b.Y);
            var y1 = Math.Max(a.Y, b.Y);
            return y1 > top && y0 < bottom;
        }

        if (Math.Abs(a.Y - b.Y) < 0.001)
        {
            var y = a.Y;
            if (y <= top || y >= bottom)
                return false;
            var x0 = Math.Min(a.X, b.X);
            var x1 = Math.Max(a.X, b.X);
            return x1 > left && x0 < right;
        }

        return false;
    }

    private static bool IsOrthogonalPath(Point[] path)
    {
        for (var i = 1; i < path.Length; i++)
        {
            if (!PointsNearlyEqual(path[i - 1], path[i])
                && !IsAxisAligned(path[i - 1], path[i]))
                return false;
        }
        return true;
    }

    private static bool IsAxisAligned(Point a, Point b)
        => Math.Abs(a.X - b.X) < 0.001 || Math.Abs(a.Y - b.Y) < 0.001;

    private static bool FirstSegmentIsVertical(Point[] path, bool vertical)
    {
        if (path.Length < 2 || PointsNearlyEqual(path[0], path[1]))
            return true;
        return SegmentIsVertical(path[0], path[1]) == vertical;
    }

    private static bool LastSegmentIsVertical(Point[] path, bool vertical)
    {
        if (path.Length < 2)
            return true;
        var n = path.Length;
        if (PointsNearlyEqual(path[n - 2], path[n - 1]))
            return true;
        return SegmentIsVertical(path[n - 2], path[n - 1]) == vertical;
    }

    private static bool SegmentIsVertical(Point a, Point b)
        => Math.Abs(a.X - b.X) < Math.Abs(a.Y - b.Y);

    private static double PathPolylineLength(IReadOnlyList<Point> path)
    {
        var len = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            if (!PointsNearlyEqual(path[i - 1], path[i]))
                len += (path[i] - path[i - 1]).Length;
        }
        return len;
    }

    private static Point[] SimplifyOrthogonalPath(Point[] path)
    {
        if (path.Length <= 2)
            return path;

        var simplified = new List<Point> { path[0] };
        for (var i = 1; i < path.Length; i++)
        {
            if (PointsNearlyEqual(path[i], simplified[^1]))
                continue;
            if (simplified.Count >= 2)
            {
                var prev = simplified[^2];
                var last = simplified[^1];
                if (IsAxisAligned(prev, last) && IsAxisAligned(last, path[i])
                    && ((Math.Abs(prev.X - last.X) < 0.001 && Math.Abs(last.X - path[i].X) < 0.001)
                        || (Math.Abs(prev.Y - last.Y) < 0.001 && Math.Abs(last.Y - path[i].Y) < 0.001)))
                {
                    simplified[^1] = path[i];
                    continue;
                }
            }
            simplified.Add(path[i]);
        }
        return simplified.ToArray();
    }

    private static bool PointsNearlyEqual(Point a, Point b)
        => Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001;

    private static bool HitTestArrowPath(Point p, IReadOnlyList<Point> path, double tolerance)
    {
        for (var i = 1; i < path.Count; i++)
        {
            if (PointNearSegment(p, path[i - 1], path[i], tolerance))
                return true;
        }
        return false;
    }
}
