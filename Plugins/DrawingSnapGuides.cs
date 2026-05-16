using System.Windows;

namespace Noted;

/// <summary>draw.io-style alignment and equal-spacing snap while moving shapes.</summary>
internal static class DrawingSnapGuides
{
    public const double DefaultSnapDistance = 8;
    private const double GapMatchTolerance = 3;

    public readonly struct GuideLine
    {
        public bool IsVertical { get; init; }
        public bool IsSpacing { get; init; }
        public double Position { get; init; }
        public double Start { get; init; }
        public double End { get; init; }
    }

    public static (double Dx, double Dy, IReadOnlyList<GuideLine> Guides) AdjustMove(
        double dx,
        double dy,
        Rect movingBounds,
        IReadOnlyList<Rect> otherBounds,
        double snapDistance = DefaultSnapDistance)
    {
        if (movingBounds.Width <= 0 && movingBounds.Height <= 0)
            return (dx, dy, Array.Empty<GuideLine>());

        var others = otherBounds
            .Where(r => r.Width > 0.5 || r.Height > 0.5)
            .ToList();
        if (others.Count == 0)
            return (dx, dy, Array.Empty<GuideLine>());

        var guides = new List<GuideLine>();
        var vSpan = ComputeVerticalSpan(movingBounds, others);
        var hSpan = ComputeHorizontalSpan(movingBounds, others);

        var proposed = Offset(movingBounds, dx, dy);
        var snapDx = SnapAlignAxis(proposed, others, vertical: false, snapDistance, guides, vSpan);
        proposed = Offset(movingBounds, dx + snapDx, dy);
        var snapDy = SnapAlignAxis(proposed, others, vertical: true, snapDistance, guides, hSpan);

        proposed = Offset(movingBounds, dx + snapDx, dy + snapDy);
        var spaceDx = SnapSpacingHorizontal(proposed, others, snapDistance, guides);
        var spaceDy = SnapSpacingVertical(proposed, others, snapDistance, guides);

        return (dx + snapDx + spaceDx, dy + snapDy + spaceDy, guides);
    }

    /// <summary>
    /// Keeps magnetic snap without locking the selection on a guide — when the user drags
    /// past a snap line, movement follows the pointer instead of being pulled back.
    /// </summary>
    public static double CoalesceMoveDelta(double rawDelta, double snappedDelta)
    {
        if (Math.Abs(rawDelta) < 0.001)
            return snappedDelta;
        if (Math.Abs(snappedDelta) < 0.001)
            return rawDelta;
        if (Math.Sign(rawDelta) != Math.Sign(snappedDelta))
            return rawDelta;
        return snappedDelta;
    }

    public static bool IsSnapEngaged(double rawDx, double rawDy, double finalDx, double finalDy)
        => Math.Abs(finalDx - rawDx) > 0.001 || Math.Abs(finalDy - rawDy) > 0.001;

    private static Rect Offset(Rect r, double dx, double dy)
        => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    private static (double Top, double Bottom) ComputeVerticalSpan(Rect moving, List<Rect> others)
    {
        var top = moving.Top;
        var bottom = moving.Bottom;
        foreach (var r in others)
        {
            top = Math.Min(top, r.Top);
            bottom = Math.Max(bottom, r.Bottom);
        }
        return (top, bottom);
    }

    private static (double Left, double Right) ComputeHorizontalSpan(Rect moving, List<Rect> others)
    {
        var left = moving.Left;
        var right = moving.Right;
        foreach (var r in others)
        {
            left = Math.Min(left, r.Left);
            right = Math.Max(right, r.Right);
        }
        return (left, right);
    }

    private enum AlignEdgeKind { Min, Mid, Max }

    private static bool IsCompatibleAlignPair(AlignEdgeKind moving, AlignEdgeKind target)
    {
        if (moving == AlignEdgeKind.Mid || target == AlignEdgeKind.Mid)
            return moving == AlignEdgeKind.Mid && target == AlignEdgeKind.Mid;
        if (moving == AlignEdgeKind.Min && target == AlignEdgeKind.Max)
            return false;
        if (moving == AlignEdgeKind.Max && target == AlignEdgeKind.Min)
            return false;
        return true;
    }

    private static double SnapAlignAxis(
        Rect proposed,
        List<Rect> others,
        bool vertical,
        double snapDistance,
        List<GuideLine> guides,
        (double Start, double End) span)
    {
        (double Pos, AlignEdgeKind Kind)[] movingEdges = vertical
            ?
            [
                (proposed.Top, AlignEdgeKind.Min),
                (proposed.Top + proposed.Height / 2, AlignEdgeKind.Mid),
                (proposed.Bottom, AlignEdgeKind.Max),
            ]
            :
            [
                (proposed.Left, AlignEdgeKind.Min),
                (proposed.Left + proposed.Width / 2, AlignEdgeKind.Mid),
                (proposed.Right, AlignEdgeKind.Max),
            ];

        double? bestDelta = null;
        double? guidePos = null;
        foreach (var (edge, movingKind) in movingEdges)
        {
            foreach (var r in others)
            {
                (double Pos, AlignEdgeKind Kind)[] targets = vertical
                    ?
                    [
                        (r.Top, AlignEdgeKind.Min),
                        (r.Top + r.Height / 2, AlignEdgeKind.Mid),
                        (r.Bottom, AlignEdgeKind.Max),
                    ]
                    :
                    [
                        (r.Left, AlignEdgeKind.Min),
                        (r.Left + r.Width / 2, AlignEdgeKind.Mid),
                        (r.Right, AlignEdgeKind.Max),
                    ];

                foreach (var (target, targetKind) in targets)
                {
                    if (!IsCompatibleAlignPair(movingKind, targetKind))
                        continue;

                    var delta = target - edge;
                    if (Math.Abs(delta) > snapDistance) continue;
                    if (bestDelta == null || Math.Abs(delta) < Math.Abs(bestDelta.Value))
                    {
                        bestDelta = delta;
                        guidePos = target;
                    }
                }
            }
        }

        if (bestDelta == null || guidePos == null)
            return 0;

        guides.Add(vertical
            ? new GuideLine { IsVertical = false, Position = guidePos.Value, Start = span.Start, End = span.End }
            : new GuideLine { IsVertical = true, Position = guidePos.Value, Start = span.Start, End = span.End });

        return bestDelta.Value;
    }

    private readonly struct GapPair
    {
        public double Size { get; init; }
        public Rect First { get; init; }
        public Rect Second { get; init; }
    }

    private static double SnapSpacingHorizontal(Rect proposed, List<Rect> others, double snapDistance, List<GuideLine> guides)
    {
        var sorted = others.OrderBy(r => r.Left).ToList();
        var gaps = new List<GapPair>();
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var size = sorted[i + 1].Left - sorted[i].Right;
            if (size >= 1)
                gaps.Add(new GapPair { Size = size, First = sorted[i], Second = sorted[i + 1] });
        }

        double? bestDx = null;
        GapPair? matched = null;
        foreach (var gap in gaps)
        {
            var before = bestDx;
            ConsiderSnap(proposed.Left, gap.First.Right + gap.Size, snapDistance, ref bestDx);
            if (bestDx != before) matched = gap;
            before = bestDx;
            ConsiderSnap(proposed.Left, gap.Second.Right + gap.Size, snapDistance, ref bestDx);
            if (bestDx != before) matched = gap;
            before = bestDx;
            ConsiderSnap(proposed.Left, gap.Second.Left - gap.Size - proposed.Width, snapDistance, ref bestDx);
            if (bestDx != before) matched = gap;
        }

        for (var i = 0; i < sorted.Count - 2; i++)
        {
            var g1 = sorted[i + 1].Left - sorted[i].Right;
            var g2 = sorted[i + 2].Left - sorted[i + 1].Right;
            if (g1 < 1 || Math.Abs(g1 - g2) > GapMatchTolerance) continue;

            var targetLeft = sorted[i + 2].Right + g1;
            var tripleDelta = targetLeft - proposed.Left;
            if (Math.Abs(tripleDelta) <= snapDistance
                && (bestDx == null || Math.Abs(tripleDelta) < Math.Abs(bestDx.Value)))
            {
                bestDx = tripleDelta;
                matched = null;
                var y = OverlapMidY(sorted[i], sorted[i + 1], sorted[i + 2]);
                guides.Add(new GuideLine { IsVertical = false, IsSpacing = true, Position = y, Start = sorted[i].Right, End = sorted[i + 1].Left });
                guides.Add(new GuideLine { IsVertical = false, IsSpacing = true, Position = y, Start = sorted[i + 1].Right, End = sorted[i + 2].Left });
            }
        }

        if (bestDx != null && matched != null)
        {
            var y = OverlapMidY(matched.Value.First, matched.Value.Second);
            guides.Add(new GuideLine { IsVertical = false, IsSpacing = true, Position = y, Start = matched.Value.First.Right, End = matched.Value.Second.Left });
        }

        return bestDx ?? 0;
    }

    private static double SnapSpacingVertical(Rect proposed, List<Rect> others, double snapDistance, List<GuideLine> guides)
    {
        var sorted = others.OrderBy(r => r.Top).ToList();
        var gaps = new List<GapPair>();
        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var size = sorted[i + 1].Top - sorted[i].Bottom;
            if (size >= 1)
                gaps.Add(new GapPair { Size = size, First = sorted[i], Second = sorted[i + 1] });
        }

        double? bestDy = null;
        GapPair? matched = null;
        foreach (var gap in gaps)
        {
            var before = bestDy;
            ConsiderSnap(proposed.Top, gap.First.Bottom + gap.Size, snapDistance, ref bestDy);
            if (bestDy != before) matched = gap;
            before = bestDy;
            ConsiderSnap(proposed.Top, gap.Second.Bottom + gap.Size, snapDistance, ref bestDy);
            if (bestDy != before) matched = gap;
            before = bestDy;
            ConsiderSnap(proposed.Top, gap.Second.Top - gap.Size - proposed.Height, snapDistance, ref bestDy);
            if (bestDy != before) matched = gap;
        }

        for (var i = 0; i < sorted.Count - 2; i++)
        {
            var g1 = sorted[i + 1].Top - sorted[i].Bottom;
            var g2 = sorted[i + 2].Top - sorted[i + 1].Bottom;
            if (g1 < 1 || Math.Abs(g1 - g2) > GapMatchTolerance) continue;

            var targetTop = sorted[i + 2].Bottom + g1;
            var tripleDelta = targetTop - proposed.Top;
            if (Math.Abs(tripleDelta) <= snapDistance
                && (bestDy == null || Math.Abs(tripleDelta) < Math.Abs(bestDy.Value)))
            {
                bestDy = tripleDelta;
                matched = null;
                var x = OverlapMidX(sorted[i], sorted[i + 1], sorted[i + 2]);
                guides.Add(new GuideLine { IsVertical = true, IsSpacing = true, Position = x, Start = sorted[i].Bottom, End = sorted[i + 1].Top });
                guides.Add(new GuideLine { IsVertical = true, IsSpacing = true, Position = x, Start = sorted[i + 1].Bottom, End = sorted[i + 2].Top });
            }
        }

        if (bestDy != null && matched != null)
        {
            var x = OverlapMidX(matched.Value.First, matched.Value.Second);
            guides.Add(new GuideLine { IsVertical = true, IsSpacing = true, Position = x, Start = matched.Value.First.Bottom, End = matched.Value.Second.Top });
        }

        return bestDy ?? 0;
    }

    private static bool ConsiderSnap(double edge, double target, double snapDistance, ref double? bestDelta)
    {
        var delta = target - edge;
        if (Math.Abs(delta) > snapDistance) return false;
        if (bestDelta == null || Math.Abs(delta) < Math.Abs(bestDelta.Value))
            bestDelta = delta;
        return true;
    }

    private static double OverlapMidY(Rect a, Rect b)
    {
        var top = Math.Max(a.Top, b.Top);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return top <= bottom ? (top + bottom) / 2 : (a.Top + a.Bottom + b.Top + b.Bottom) / 4;
    }

    private static double OverlapMidY(Rect a, Rect b, Rect c)
    {
        var top = Math.Max(Math.Max(a.Top, b.Top), c.Top);
        var bottom = Math.Min(Math.Min(a.Bottom, b.Bottom), c.Bottom);
        return top <= bottom ? (top + bottom) / 2 : (a.Top + b.Top + c.Top) / 3;
    }

    private static double OverlapMidX(Rect a, Rect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var right = Math.Min(a.Right, b.Right);
        return left <= right ? (left + right) / 2 : (a.Left + a.Right + b.Left + b.Right) / 4;
    }

    private static double OverlapMidX(Rect a, Rect b, Rect c)
    {
        var left = Math.Max(Math.Max(a.Left, b.Left), c.Left);
        var right = Math.Min(Math.Min(a.Right, b.Right), c.Right);
        return left <= right ? (left + right) / 2 : (a.Left + b.Left + c.Left) / 3;
    }
}
