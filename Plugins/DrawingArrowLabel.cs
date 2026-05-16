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

    private static (Point Midpoint, double AngleDeg, double Length) GetArrowLineMetrics(Point p1, Point p2)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var angleDeg = length < 0.001
            ? 0
            : NormalizeArrowLabelAngleDegrees(Math.Atan2(dy, dx) * 180.0 / Math.PI);
        var midpoint = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        return (midpoint, angleDeg, length);
    }

    private static Size MeasureArrowLabel(DrawItem arrow, string? text, Visual visual)
    {
        var (_, _, length) = GetArrowLineMetrics(arrow.P1, arrow.P2);
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

    private static void LayoutArrowLabelElement(FrameworkElement element, DrawItem arrow, Size size)
    {
        var (mid, angleDeg, _) = GetArrowLineMetrics(arrow.P1, arrow.P2);
        element.Width = size.Width;
        element.Height = size.Height;
        Canvas.SetLeft(element, mid.X - size.Width / 2);
        Canvas.SetTop(element, mid.Y - size.Height / 2);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new RotateTransform(angleDeg);
    }

    private string? GetArrowLabelText(DrawItem arrow)
    {
        if (ReferenceEquals(arrow, _editingItem) && _activeEditor is TextBox box)
            return box.Text;
        return arrow.Text;
    }

    private bool TryGetArrowLabelGap(DrawItem arrow, Visual visual, out double gapHalfLength)
    {
        gapHalfLength = 0;
        var text = GetArrowLabelText(arrow);
        if (string.IsNullOrEmpty(text))
            return false;

        var (_, _, length) = GetArrowLineMetrics(arrow.P1, arrow.P2);
        if (length < 8)
            return false;

        var size = MeasureArrowLabel(arrow, text, visual);
        var textWidth = Math.Max(8, size.Width - 12);
        gapHalfLength = textWidth / 2 + 6;
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
        var p1 = it.P1;
        var p2 = it.P2;
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
            return;

        var ux = dx / len;
        var uy = dy / len;

        if (TryGetArrowLabelGap(it, surface, out var halfGap))
        {
            var midX = (p1.X + p2.X) / 2;
            var midY = (p1.Y + p2.Y) / 2;
            var gapStart = new Point(midX - ux * halfGap, midY - uy * halfGap);
            var gapEnd = new Point(midX + ux * halfGap, midY + uy * halfGap);
            AddArrowLineSegment(surface, it, p1, gapStart);
            AddArrowLineSegment(surface, it, gapEnd, p2);
            return;
        }

        AddArrowLineSegment(surface, it, p1, p2);
    }

    private void RenderArrowLabel(DrawItem it, Canvas surface)
    {
        var text = GetArrowLabelText(it);
        if (string.IsNullOrEmpty(text))
            return;

        var (_, _, length) = GetArrowLineMetrics(it.P1, it.P2);
        if (length < 8)
            return;

        var maxWidth = Math.Max(24, length - 24);
        var tb = new TextBlock
        {
            Text = it.Text,
            FontSize = it.FontSize,
            FontFamily = it.FontFamily,
            Foreground = it.TextColor,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
            IsHitTestVisible = false,
        };
        var size = MeasureArrowLabel(it, text, surface);
        tb.Width = size.Width - 12;
        tb.Measure(new Size(tb.Width, double.PositiveInfinity));
        LayoutArrowLabelElement(tb, it, size);
        surface.Children.Add(tb);
    }
}
