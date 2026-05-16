using System.Windows;

namespace Noted;

internal static class DrawingShapeSizeSnap
{
    public static Point SnapDragCorner(Point p1, Point p2, double standardWidth, double standardHeight, double thresholdPx)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var w = Math.Abs(dx);
        var h = Math.Abs(dy);
        if (w < 3 && h < 3)
            return p2;

        var snappedW = SnapDimension(w, standardWidth, thresholdPx);
        var snappedH = SnapDimension(h, standardHeight, thresholdPx);
        var signX = Math.Abs(dx) < 0.001 ? 1 : Math.Sign(dx);
        var signY = Math.Abs(dy) < 0.001 ? 1 : Math.Sign(dy);
        return new Point(p1.X + signX * snappedW, p1.Y + signY * snappedH);
    }

    private static double SnapDimension(double value, double standard, double threshold)
    {
        if (value < 3)
            return value;
        if (Math.Abs(value - standard) <= threshold)
            return standard;
        return value;
    }
}
