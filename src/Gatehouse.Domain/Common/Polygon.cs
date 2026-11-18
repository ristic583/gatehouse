namespace Gatehouse.Domain.Common;

public sealed record Polygon(IReadOnlyList<Point> Points)
{
    public static Result<Polygon> Create(IEnumerable<Point> points, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.ToList();

        if (list.Count < 3)
        {
            return Invalid("A polygon needs at least three points.");
        }

        if (list.Any(p => double.IsNaN(p.X) || double.IsNaN(p.Y) || p.X < 0 || p.Y < 0 || p.X > width || p.Y > height))
        {
            return Invalid("Every polygon point must lie inside the floor plan.");
        }

        var n = list.Count;
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var adjacent = j == i + 1 || (i == 0 && j == n - 1);
                if (adjacent)
                {
                    continue;
                }

                if (SegmentsIntersect(list[i], list[(i + 1) % n], list[j], list[(j + 1) % n]))
                {
                    return Invalid("The polygon must not intersect itself.");
                }
            }
        }

        return new Polygon(list.AsReadOnly());
    }

    public bool Equals(Polygon? other) => other is not null && Points.SequenceEqual(other.Points);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    private static Error Invalid(string message) => Error.Validation("area.invalid_polygon", message);

    private static bool SegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        var d1 = Cross(p3, p4, p1);
        var d2 = Cross(p3, p4, p2);
        var d3 = Cross(p1, p2, p3);
        var d4 = Cross(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        return (d1 == 0 && OnSegment(p3, p4, p1))
            || (d2 == 0 && OnSegment(p3, p4, p2))
            || (d3 == 0 && OnSegment(p1, p2, p3))
            || (d4 == 0 && OnSegment(p1, p2, p4));
    }

    private static double Cross(Point a, Point b, Point c) => ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static bool OnSegment(Point a, Point b, Point p) =>
        Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
}
