using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>
/// Approximate overlap of two simple polygons as a fraction of the smaller one (sampled on a 200×200 grid over the
/// bounding-box intersection); good enough for the 1 % rule of <c>domain-model.md</c> §2.10.
/// </summary>
public static class PolygonOverlap
{
    public const double MaxFraction = 0.01;
    private const int Grid = 200;

    public static double Fraction(Polygon a, Polygon b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var minX = Math.Max(a.Points.Min(p => p.X), b.Points.Min(p => p.X));
        var maxX = Math.Min(a.Points.Max(p => p.X), b.Points.Max(p => p.X));
        var minY = Math.Max(a.Points.Min(p => p.Y), b.Points.Min(p => p.Y));
        var maxY = Math.Min(a.Points.Max(p => p.Y), b.Points.Max(p => p.Y));
        if (maxX <= minX || maxY <= minY)
        {
            return 0;
        }

        var dx = (maxX - minX) / Grid;
        var dy = (maxY - minY) / Grid;
        var inside = 0;
        for (var i = 0; i < Grid; i++)
        {
            for (var j = 0; j < Grid; j++)
            {
                var p = new Point(minX + ((i + 0.5) * dx), minY + ((j + 0.5) * dy));
                if (Contains(a, p) && Contains(b, p))
                {
                    inside++;
                }
            }
        }

        var overlap = inside * dx * dy;
        var smaller = Math.Min(Area(a), Area(b));
        return smaller <= 0 ? 0 : overlap / smaller;
    }

    public static bool Contains(Polygon polygon, Point p)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var pts = polygon.Points;
        var inside = false;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            if ((pts[i].Y > p.Y) != (pts[j].Y > p.Y)
                && p.X < ((pts[j].X - pts[i].X) * (p.Y - pts[i].Y) / (pts[j].Y - pts[i].Y)) + pts[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static double Area(Polygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var pts = polygon.Points;
        double sum = 0;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            sum += (pts[j].X * pts[i].Y) - (pts[i].X * pts[j].Y);
        }

        return Math.Abs(sum) / 2;
    }
}
