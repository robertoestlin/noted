using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Noted;

internal sealed partial class DrawingWindow
{
    private const double AnchorSnapRadius = 14;
    private const double AnchorDotRadius = 5.5;

    private void AssignItemId(DrawItem item)
    {
        if (item.Id == 0)
            item.Id = _nextItemId++;
    }

    private void EnsureItemIds()
    {
        foreach (var it in _items)
        {
            if (it.Id == 0)
                AssignItemId(it);
        }
    }

    private DrawItem? FindShapeById(int id)
    {
        foreach (var it in _items)
        {
            if (it.Id == id)
                return it;
        }
        return null;
    }

    private static bool IsAnchorableShape(DrawItem it)
        => it.Kind is "rect" or "ellipse" or "diamond" or "domain" or "actor" or "db";

    private DrawItem? HitTestAnchorableShape(Point p)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (!IsAnchorableShape(it)) continue;
            if (HitTest(it, p))
                return it;
        }
        return null;
    }

    private bool TryGetNearestAnchorSnap(Point p, out DrawItem? shape, out Point anchor)
    {
        shape = null;
        anchor = default;
        var bestDist = AnchorSnapRadius;
        foreach (var it in _items)
        {
            if (!IsAnchorableShape(it)) continue;
            foreach (var pt in GetShapeAnchorPoints(it))
            {
                var d = (p - pt).Length;
                if (d < bestDist)
                {
                    bestDist = d;
                    shape = it;
                    anchor = pt;
                }
            }
        }
        return shape != null;
    }

    private DrawItem? FindShapeOwningAnchorPoint(Point p, double tolerance = AnchorSnapRadius)
    {
        foreach (var it in _items)
        {
            if (!IsAnchorableShape(it)) continue;
            foreach (var pt in GetShapeAnchorPoints(it))
            {
                if ((p - pt).Length <= tolerance)
                    return it;
            }
        }
        return null;
    }

    private static Point GetShapeCenter(DrawItem shape)
    {
        var b = GetBounds(shape);
        return new Point(b.Left + b.Width / 2, b.Top + b.Height / 2);
    }

    private static Point[] GetShapeAnchorPoints(DrawItem shape)
    {
        var b = GetBounds(shape);
        if (b.Width < 1 || b.Height < 1)
            return Array.Empty<Point>();

        if (shape.Kind == "actor")
        {
            // Actor side-middle anchors land near the body's vertical centreline (just outside
            // the head circle) instead of the far-away bbox left/right, so an incoming arrow
            // visually connects to the figure rather than to empty space beside it. The
            // vertical position of those anchors is left at the bbox middle on purpose — the
            // user prefers the original Y so horizontal arrows still hit the actor where they
            // always did. Top/bottom and corner anchors keep their bbox positions; that
            // matches the look the user had before this tweak.
            var labelHeight = Math.Min(Math.Max(b.Height * 0.22, 14), Math.Max(0, b.Height - 8));
            var figureH = Math.Max(8, b.Height - labelHeight - 2);
            var headDiameter = Math.Min(Math.Min(figureH * 0.30, b.Width * 0.6), Math.Max(8, figureH));
            var headRadius = headDiameter / 2;
            var cx = b.Left + b.Width / 2;
            var cy = b.Top + b.Height / 2;
            return new[]
            {
                new Point(b.Left, b.Top),
                new Point(cx, b.Top),
                new Point(b.Right, b.Top),
                new Point(cx + headRadius, cy),
                new Point(b.Right, b.Bottom),
                new Point(cx, b.Bottom),
                new Point(b.Left, b.Bottom),
                new Point(cx - headRadius, cy),
            };
        }

        if (shape.Kind is "rect" or "domain" or "db")
        {
            var cx = b.Left + b.Width / 2;
            var cy = b.Top + b.Height / 2;
            return new[]
            {
                new Point(b.Left, b.Top),
                new Point(cx, b.Top),
                new Point(b.Right, b.Top),
                new Point(b.Right, cy),
                new Point(b.Right, b.Bottom),
                new Point(cx, b.Bottom),
                new Point(b.Left, b.Bottom),
                new Point(b.Left, cy),
            };
        }

        if (shape.Kind == "diamond")
        {
            // Four corner vertices + four edge midpoints, all sitting on the diamond outline.
            var cx = b.Left + b.Width / 2;
            var cy = b.Top + b.Height / 2;
            var midLeftX = b.Left + b.Width / 4;
            var midRightX = b.Left + 3 * b.Width / 4;
            var midTopY = b.Top + b.Height / 4;
            var midBottomY = b.Top + 3 * b.Height / 4;
            return new[]
            {
                new Point(cx, b.Top),
                new Point(midRightX, midTopY),
                new Point(b.Right, cy),
                new Point(midRightX, midBottomY),
                new Point(cx, b.Bottom),
                new Point(midLeftX, midBottomY),
                new Point(b.Left, cy),
                new Point(midLeftX, midTopY),
            };
        }

        var center = GetShapeCenter(shape);
        var rx = b.Width / 2;
        var ry = b.Height / 2;
        var pts = new Point[8];
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            pts[i] = new Point(
                center.X + rx * Math.Cos(angle),
                center.Y + ry * Math.Sin(angle));
        }
        return pts;
    }

    private static Point GetShapeAnchorPoint(DrawItem shape, Point toward)
    {
        var anchors = GetShapeAnchorPoints(shape);
        if (anchors.Length == 0)
            return toward;

        var center = GetShapeCenter(shape);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        if (Math.Abs(dx) + Math.Abs(dy) < 0.001)
            return anchors[0];

        var best = anchors[0];
        var bestDot = double.NegativeInfinity;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var ux = dx / len;
        var uy = dy / len;
        for (var i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            var ax = a.X - center.X;
            var ay = a.Y - center.Y;
            var alen = Math.Sqrt(ax * ax + ay * ay);
            if (alen < 0.001) continue;
            var dot = (ax / alen) * ux + (ay / alen) * uy;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = a;
            }
        }
        return best;
    }

    private static int GetNearestAnchorIndex(DrawItem shape, Point p)
    {
        var anchors = GetShapeAnchorPoints(shape);
        if (anchors.Length == 0)
            return 0;

        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < anchors.Length; i++)
        {
            var d = (p - anchors[i]).Length;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private static Point GetShapeAnchorPointByIndex(DrawItem shape, int index)
    {
        var anchors = GetShapeAnchorPoints(shape);
        if (anchors.Length == 0)
            return GetShapeCenter(shape);
        index = Math.Clamp(index, 0, anchors.Length - 1);
        return anchors[index];
    }

    private void CommitArrowAnchors(DrawItem arrow)
    {
        if (arrow.Kind != "arrow") return;

        if (arrow.AnchorStartShapeId is int startId)
        {
            var startShape = FindShapeById(startId);
            if (startShape == null)
            {
                arrow.AnchorStartShapeId = null;
                arrow.AnchorStartIndex = null;
            }
            else
            {
                if (!arrow.AnchorStartIndex.HasValue)
                    arrow.AnchorStartIndex = GetNearestAnchorIndex(startShape, arrow.P1);
                arrow.P1 = GetShapeAnchorPointByIndex(startShape, arrow.AnchorStartIndex.Value);
            }
        }

        if (arrow.AnchorEndShapeId is int endId)
        {
            var endShape = FindShapeById(endId);
            if (endShape == null)
            {
                arrow.AnchorEndShapeId = null;
                arrow.AnchorEndIndex = null;
            }
            else
            {
                if (!arrow.AnchorEndIndex.HasValue)
                    arrow.AnchorEndIndex = GetNearestAnchorIndex(endShape, arrow.P2);
                arrow.P2 = GetShapeAnchorPointByIndex(endShape, arrow.AnchorEndIndex.Value);
            }
        }
    }

    private void RefreshAnchoredArrowEndpoints(DrawItem arrow)
    {
        if (arrow.Kind != "arrow") return;

        if (arrow.AnchorStartShapeId is int startId)
        {
            var startShape = FindShapeById(startId);
            if (startShape == null)
            {
                arrow.AnchorStartShapeId = null;
                arrow.AnchorStartIndex = null;
            }
            else
            {
                var idx = arrow.AnchorStartIndex ?? GetNearestAnchorIndex(startShape, arrow.P1);
                arrow.AnchorStartIndex = idx;
                arrow.P1 = GetShapeAnchorPointByIndex(startShape, idx);
            }
        }

        if (arrow.AnchorEndShapeId is int endId)
        {
            var endShape = FindShapeById(endId);
            if (endShape == null)
            {
                arrow.AnchorEndShapeId = null;
                arrow.AnchorEndIndex = null;
            }
            else
            {
                var idx = arrow.AnchorEndIndex ?? GetNearestAnchorIndex(endShape, arrow.P2);
                arrow.AnchorEndIndex = idx;
                arrow.P2 = GetShapeAnchorPointByIndex(endShape, idx);
            }
        }

        SyncArrowRouteBendsToAnchors(arrow);
    }

    private void RefreshAllAnchoredArrows(DrawItem? skipItem = null)
    {
        foreach (var it in _items)
        {
            if (ReferenceEquals(it, _drawingItem) || ReferenceEquals(it, skipItem)) continue;
            if (it.Kind == "arrow"
                && (it.AnchorStartShapeId.HasValue || it.AnchorEndShapeId.HasValue))
                RefreshAnchoredArrowEndpoints(it);
        }
    }

    private void RemoveArrowsAnchoredTo(int shapeId)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Kind != "arrow") continue;
            if (it.AnchorStartShapeId == shapeId || it.AnchorEndShapeId == shapeId)
                _items.RemoveAt(i);
        }
    }

    private void RenderShapeAnchorPoints(DrawItem shape)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        var ring = new SolidColorBrush(Colors.White);
        foreach (var pt in GetShapeAnchorPoints(shape))
        {
            var d = AnchorDotRadius * 2;
            var dot = new Ellipse
            {
                Width = d,
                Height = d,
                Fill = accent,
                Stroke = ring,
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, pt.X - AnchorDotRadius);
            Canvas.SetTop(dot, pt.Y - AnchorDotRadius);
            _overlay.Children.Add(dot);
        }
    }

    private Point ResolveArrowEndpoint(
        Point cursor,
        DrawItem? lockedShape,
        int? lockedIndex,
        out DrawItem? anchoredShape,
        out int? anchorIndex)
    {
        if (TryGetNearestAnchorSnap(cursor, out var snapShape, out var snapPoint))
        {
            anchoredShape = snapShape;
            anchorIndex = GetNearestAnchorIndex(snapShape!, snapPoint);
            return snapPoint;
        }

        var overShape = HitTestAnchorableShape(cursor);
        if (overShape == null)
        {
            anchoredShape = null;
            anchorIndex = null;
            return cursor;
        }

        if (lockedShape != null
            && overShape.Id == lockedShape.Id
            && lockedIndex.HasValue)
        {
            anchoredShape = overShape;
            anchorIndex = lockedIndex;
            return GetShapeAnchorPointByIndex(overShape, lockedIndex.Value);
        }

        anchoredShape = null;
        anchorIndex = null;
        return cursor;
    }

    private void MoveArrowEndpoint(DrawItem arrow, bool isStart, Point cursor, bool preserveAnchorLock = true)
    {
        DrawItem? lockedShape = null;
        int? lockedIndex = null;
        if (preserveAnchorLock)
        {
            if (isStart)
            {
                if (arrow.AnchorStartShapeId is int startId)
                    lockedShape = FindShapeById(startId);
                lockedIndex = arrow.AnchorStartIndex;
            }
            else
            {
                if (arrow.AnchorEndShapeId is int endId)
                    lockedShape = FindShapeById(endId);
                lockedIndex = arrow.AnchorEndIndex;
            }
        }

        var pt = ResolveArrowEndpoint(cursor, lockedShape, lockedIndex, out var shape, out var index);
        if (isStart)
        {
            arrow.P1 = pt;
            arrow.AnchorStartShapeId = shape?.Id;
            arrow.AnchorStartIndex = index;
        }
        else
        {
            arrow.P2 = pt;
            arrow.AnchorEndShapeId = shape?.Id;
            arrow.AnchorEndIndex = index;
        }
    }
}
