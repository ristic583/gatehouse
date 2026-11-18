using System.Reflection;
using System.Runtime.CompilerServices;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.UnitTests.Architecture;

/// <summary>conventions.md §1: dependencies point inward; Api endpoints use handlers, not databases.</summary>
public sealed class LayerDependencyTests
{
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Domain = typeof(Gatehouse.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(ITenantDb).Assembly;
    private static readonly Assembly Infrastructure = typeof(TenantDbContext).Assembly;
    private static readonly Assembly Api = typeof(Gatehouse.Api.Features.Platform.PlatformEndpoints).Assembly;

    private static readonly Type[] DataAccess = [typeof(DbContext), typeof(TenantDbContext), typeof(CatalogDbContext), typeof(ITenantDb)];

    [Fact]
    public void Domain_references_no_other_gatehouse_assembly() =>
        Assert.Empty(GatehouseReferences(Domain));

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api() =>
        Assert.DoesNotContain(GatehouseReferences(Application), r => r == Infrastructure.GetName().Name || r == Api.GetName().Name);

    [Fact]
    public void Infrastructure_does_not_reference_api() =>
        Assert.DoesNotContain(Api.GetName().Name, GatehouseReferences(Infrastructure));

    [Fact]
    public void Api_features_do_not_use_db_contexts_or_tenant_db() =>
        AllowList.AssertCovers(AllowList.ApiDataAccess, ApiDataAccess());

    /// <summary>Entries "{declaring type}: {parameter type}"; lambdas and local functions count for their outer type.</summary>
    internal static IReadOnlyCollection<string> ApiDataAccess() =>
        Api.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Gatehouse.Api.Features", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetConstructors(All).Cast<MethodBase>().Concat(t.GetMethods(All))
                .SelectMany(m => m.GetParameters())
                .Where(p => DataAccess.Any(d => d.IsAssignableFrom(p.ParameterType)))
                .Select(p => $"{Outermost(t).FullName}: {p.ParameterType.Name}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static List<string?> GatehouseReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Where(r => r.Name?.StartsWith("Gatehouse.", StringComparison.Ordinal) == true)
            .Select(r => r.Name)
            .ToList();

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer && (type.IsDefined(typeof(CompilerGeneratedAttribute)) || type.Name.Contains('<', StringComparison.Ordinal)))
            type = outer;
        return type;
    }
}
