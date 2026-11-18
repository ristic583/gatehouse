using System.Text.Json;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Conventions;

public static class JsonValueConversions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<Polygon, string> PolygonConverter { get; } = new(
        p => JsonSerializer.Serialize(p.Points, Options),
        s => new Polygon(JsonSerializer.Deserialize<List<Point>>(s, Options)!));

    public static ValueComparer<Polygon> PolygonComparer { get; } = new(
        (a, b) => a!.Points.SequenceEqual(b!.Points),
        p => p.GetHashCode(),
        p => new Polygon(p.Points.ToList()));

    public static ValueConverter<IReadOnlyCollection<T>, string> ListConverter<T>() => new(
        v => JsonSerializer.Serialize(v, Options),
        s => JsonSerializer.Deserialize<List<T>>(s, Options)!);

    public static ValueComparer<IReadOnlyCollection<T>> ListComparer<T>() => new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
        v => v.ToList());

    public static ValueConverter<T, string> ObjectConverter<T>()
        where T : class? => new(
        v => JsonSerializer.Serialize(v, Options),
        s => JsonSerializer.Deserialize<T>(s, Options)!);

    public static ValueComparer<T> ObjectComparer<T>()
        where T : class? => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
}
