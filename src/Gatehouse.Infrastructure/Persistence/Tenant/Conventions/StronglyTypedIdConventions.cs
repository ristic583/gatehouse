using System.Linq.Expressions;
using System.Reflection;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Conventions;

public static class StronglyTypedIdConventions
{
    public static IReadOnlyList<Type> IdTypes { get; } = typeof(Entity<>).Assembly
        .GetTypes()
        .Where(t => t.IsValueType && t.Name.EndsWith("Id", StringComparison.Ordinal)
            && t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(Guid)
            && t.GetConstructor([typeof(Guid)]) is not null)
        .ToList();

    public static void AddStronglyTypedIdConverters(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        foreach (var idType in IdTypes)
        {
            configurationBuilder.Properties(idType).HaveConversion(typeof(StronglyTypedIdConverter<>).MakeGenericType(idType));
        }
    }
}

#pragma warning disable CA1812 // Instantiated by EF Core through reflection.
internal sealed class StronglyTypedIdConverter<TId> : ValueConverter<TId, Guid>
#pragma warning restore CA1812
    where TId : struct
{
    public StronglyTypedIdConverter()
        : base(ToGuid(), FromGuid())
    {
    }

    private static Expression<Func<TId, Guid>> ToGuid()
    {
        var p = Expression.Parameter(typeof(TId), "id");
        return Expression.Lambda<Func<TId, Guid>>(Expression.Property(p, "Value"), p);
    }

    private static Expression<Func<Guid, TId>> FromGuid()
    {
        var g = Expression.Parameter(typeof(Guid), "value");
        return Expression.Lambda<Func<Guid, TId>>(Expression.New(typeof(TId).GetConstructor([typeof(Guid)])!, g), g);
    }
}
