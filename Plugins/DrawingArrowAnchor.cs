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
        => it.Kind is "rect" or "ellipse";

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

        if (shape.Kind == "rect")
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
        foreach (var a in anchors)
        {
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

    private void RefreshAnchoredArrowEndpoints(DrawItem arrow)
    {
        if (arrow.Kind != "arrow") return;

        if (arrow.AnchorStartShapeId is int startId)
        {
            var startShape = FindShapeById(startId);
            if (startShape != null)
                arrow.P1 = GetShapeAnchorPoint(startShape, arrow.P2);
            else
                arrow.AnchorStartShapeId = null;
        }

        if (arrow.AnchorEndShapeId is int endId)
        {
            var endShape = FindShapeById(endId);
            if (endShape != null)
                arrow.P2 = GetShapeAnchorPoint(endShape, arrow.P1);
            else
                arrow.AnchorEndShapeId = null;
        }
    }

    private void RefreshAllAnchoredArrows()
    {
        foreach (var it in _items)
        {
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

    private Point ResolveArrowEndpoint(Point cursor, Point otherEnd, out DrawItem? anchoredShape)
    {
        if (TryGetNearestAnchorSnap(cursor, out var snapShape, out var snapPoint))
        {
            anchoredShape = snapShape;
            return snapPoint;
        }

        anchoredShape = HitTestAnchorableShape(cursor);
        return anchoredShape != null
            ? GetShapeAnchorPoint(anchoredShape, otherEnd)
            : cursor;
    }
}
