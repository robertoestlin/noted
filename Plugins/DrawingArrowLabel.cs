using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

internal sealed partial class DrawingWindow
{
    private static double NormalizeArrowLabelAngleDegrees(double angleDeg)
    {
        while (angleDeg > 90) angleDeg -= 180;
        while (angleDeg < -90) angleDeg += 180;
        return angleDeg;
    }

    /// <summary>Returns the effective tilt-threshold (deg) above which an arrow's label is rendered
    /// horizontally. Per-arrow value (copied from the variant) wins when non-zero; otherwise the
    /// global <c>Settings → Drawing → Arrow label horizontal angle</c> default applies. <c>0</c>
    /// always means "no horizontal flip — follow the line".</summary>
    private double GetEffectiveArrowLabelHorizontalThreshold(DrawItem arrow)
    {
        if (arrow.HorizontalLabelAngleDeg > 0)
            return arrow.HorizontalLabelAngleDeg;
        return _host.DrawingArrowHorizontalLabelAngleDeg;
    }

    /// <summary>Applies the threshold rule to a (already-normalized) line angle: returns 0 if the
    /// line tilts more steeply than the threshold, otherwise returns the line angle unchanged.
    /// A non-positive threshold disables the flip entirely.</summary>
    private static double ApplyArrowLabelHorizontalThreshold(double angleDeg, double thresholdDeg)
    {
        if (thresholdDeg <= 0) return angleDeg;
        return Math.Abs(angleDeg) > thresholdDeg ? 0 : angleDeg;
    }

    /// <summary>Resolves the actual on-screen angle for an arrow's label by combining the path's
    /// natural tilt with the effective horizontal-flip threshold.</summary>
    private double GetArrowLabelDisplayAngle(DrawItem arrow, double pathAngleDeg)
        => ApplyArrowLabelHorizontalThreshold(pathAngleDeg, GetEffectiveArrowLabelHorizontalThreshold(arrow));

    private static (Point Midpoint, double AngleDeg, double Length) GetArrowPathMetrics(IReadOnlyList<Point> path)
    {
        var length = PathPolylineLength(path);
        if (path.Count < 2 || length < 0.001)
        {
            var p = path.Count > 0 ? path[0] : default;
            return (p, 0, 0);
        }

        var half = length / 2;
        var walked = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var segLen = (b - a).Length;
            if (segLen < 0.001)
                continue;

            if (walked + segLen >= half)
            {
                var t = segLen < 0.001 ? 0 : (half - walked) / segLen;
                var mid = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var angleDeg = NormalizeArrowLabelAngleDegrees(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                return (mid, angleDeg, length);
            }
            walked += segLen;
        }

        var last = path[^1];
        var prev = path[^2];
        var ldx = last.X - prev.X;
        var ldy = last.Y - prev.Y;
        var lastAngle = Math.Sqrt(ldx * ldx + ldy * ldy) < 0.001
            ? 0
            : NormalizeArrowLabelAngleDegrees(Math.Atan2(ldy, ldx) * 180.0 / Math.PI);
        return (last, lastAngle, length);
    }

    private static Size MeasureArrowLabel(DrawItem arrow, string? text, Visual visual, IReadOnlyList<Point> path)
    {
        var (_, _, length) = GetArrowPathMetrics(path);
        var maxWidth = Math.Max(24, length - 24);
        var dpi = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        var display = string.IsNullOrEmpty(text) ? " " : text;
        var typeface = new Typeface(arrow.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(
            display,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            arrow.FontSize,
            Brushes.Black,
            dpi)
        {
            MaxTextWidth = maxWidth,
        };
        var w = Math.Min(maxWidth, Math.Max(ft.Width, 8)) + 12;
        var h = Math.Max(arrow.FontSize + 8, ft.Height + 8);
        return new Size(Math.Max(32, w), h);
    }

    private static void LayoutArrowLabelElement(FrameworkElement element, Point midpoint, double angleDeg, Size size)
    {
        element.Width = size.Width;
        element.Height = size.Height;
        Canvas.SetLeft(element, midpoint.X - size.Width / 2);
        Canvas.SetTop(element, midpoint.Y - size.Height / 2);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new RotateTransform(angleDeg);
    }

    private string? GetArrowLabelText(DrawItem arrow)
    {
        if (ReferenceEquals(arrow, _editingItem) && _activeEditor is TextBox box)
            return box.Text;
        return arrow.Text;
    }

    private bool TryGetArrowLabelGap(DrawItem arrow, Visual visual, IReadOnlyList<Point> path, out double gapHalfLength)
    {
        gapHalfLength = 0;
        var text = GetArrowLabelText(arrow);
        if (string.IsNullOrEmpty(text))
            return false;

        var (_, pathAngleDeg, length) = GetArrowPathMetrics(path);
        if (length < 8)
            return false;

        var size = MeasureArrowLabel(arrow, text, visual, path);
        // The label is an axis-aligned box of size.Width × size.Height (centered on the line's
        // midpoint) rotated by displayAngle. We need the half-length of the line segment that
        // actually intersects this rotated box — i.e. how far along the line, in either direction
        // from the midpoint, the shaft is hidden by the label.
        //
        // In the label's local frame the line passes through the origin at angle θ = pathAngleDeg
        // − displayAngle. It exits the box through either the W or H edge — whichever it reaches
        // first — at parameter min(W/(2|cos θ|), H/(2|sin θ|)). That's a much tighter (and
        // geometrically correct) clearance than the bounding-box projection W|cos θ| + H|sin θ|,
        // which would also clear the rotated box's empty corners.
        var displayAngle = GetArrowLabelDisplayAngle(arrow, pathAngleDeg);
        var relRad = (pathAngleDeg - displayAngle) * Math.PI / 180.0;
        var cosAbs = Math.Abs(Math.Cos(relRad));
        var sinAbs = Math.Abs(Math.Sin(relRad));
        double intersectHalf;
        const double epsilon = 1e-6;
        if (cosAbs < epsilon)
            intersectHalf = size.Height / 2;
        else if (sinAbs < epsilon)
            intersectHalf = size.Width / 2;
        else
            intersectHalf = Math.Min(size.Width / (2 * cosAbs), size.Height / (2 * sinAbs));
        gapHalfLength = intersectHalf;
        var maxHalf = (length - 20) / 2;
        if (maxHalf > 0 && gapHalfLength > maxHalf)
            gapHalfLength = maxHalf;
        return gapHalfLength > 2;
    }

    private static void AddArrowLineSegment(Canvas surface, DrawItem it, Point a, Point b)
    {
        surface.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = a.X,
            Y1 = a.Y,
            X2 = b.X,
            Y2 = b.Y,
            Stroke = it.Stroke,
            StrokeThickness = it.StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
    }

    private void RenderArrowShaft(DrawItem it, Canvas surface)
    {
        var path = GetArrowPathPoints(it);
        if (path.Count < 2)
            return;

        if (TryGetArrowLabelGap(it, surface, path, out var halfGap))
        {
            var length = PathPolylineLength(path);
            var gapStart = length / 2 - halfGap;
            var gapEnd = length / 2 + halfGap;
            if (gapStart > 0 && gapEnd < length
                && TrySplitPathAtLength(path, gapStart, out var beforeGap, out _)
                && TrySplitPathAtLength(path, gapEnd, out _, out var afterGap))
            {
                if (beforeGap.Count >= 2)
                    RenderArrowPolyline(it, surface, beforeGap);
                if (afterGap.Count >= 2)
                    RenderArrowPolyline(it, surface, afterGap);
                return;
            }
        }

        RenderArrowPolyline(it, surface, path);
    }

    private static void RenderArrowPolyline(DrawItem it, Canvas surface, IReadOnlyList<Point> path)
    {
        for (var i = 1; i < path.Count; i++)
        {
            if (PointsNearlyEqual(path[i - 1], path[i]))
                continue;
            AddArrowLineSegment(surface, it, path[i - 1], path[i]);
        }
    }

    private static bool TrySplitPathAtLength(
        IReadOnlyList<Point> path,
        double lengthFromStart,
        out List<Point> before,
        out List<Point> after)
    {
        before = new List<Point>();
        after = new List<Point>();
        if (path.Count < 2)
            return false;

        before.Add(path[0]);
        var walked = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var segLen = (b - a).Length;
            if (segLen < 0.001)
                continue;

            if (walked + segLen >= lengthFromStart)
            {
                var t = (lengthFromStart - walked) / segLen;
                var split = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                before.Add(split);
                after.Add(split);
                for (var j = i; j < path.Count; j++)
                    after.Add(path[j]);
                return before.Count >= 2 && after.Count >= 2;
            }

            before.Add(b);
            walked += segLen;
        }

        return false;
    }

    private void RenderArrowLabel(DrawItem it, Canvas surface)
    {
        var text = GetArrowLabelText(it);
        if (string.IsNullOrEmpty(text))
            return;

        var path = GetArrowPathPoints(it);
        var (mid, pathAngleDeg, length) = GetArrowPathMetrics(path);
        if (length < 8)
            return;

        var size = MeasureArrowLabel(it, text, surface, path);
        // Width and Padding are baked into size here (size.Width = textWidth + 12). We pass that
        // same Size to LayoutArrowLabelElement so the on-canvas box width matches what gap-around-text
        // assumes in TryGetArrowLabelGap — guaranteeing the glyph centroid sits exactly on the line
        // midpoint regardless of font / font size.
        var tb = new TextBlock
        {
            Text = text,
            FontSize = it.FontSize,
            FontFamily = it.FontFamily,
            Foreground = it.TextColor,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0),
            IsHitTestVisible = false,
        };
        var displayAngle = GetArrowLabelDisplayAngle(it, pathAngleDeg);
        LayoutArrowLabelElement(tb, mid, displayAngle, size);
        surface.Children.Add(tb);
    }
}
