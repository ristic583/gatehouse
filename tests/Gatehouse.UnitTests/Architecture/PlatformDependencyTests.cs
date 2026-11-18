using System.Reflection;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Platform;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.UnitTests.Architecture;

/// <summary>authorization.md §11 rule 2: platform code is catalog-only.</summary>
public sealed class PlatformDependencyTests
{
    private static readonly Type[] Forbidden = [typeof(ITenantDb), typeof(ITenantContext), typeof(ITenantScopeFactory)];

    [Fact]
    public void Platform_handlers_do_not_depend_on_tenant_db()
    {
        var types = typeof(IPlatformOperator).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Gatehouse.Application.Platform", StringComparison.Ordinal) == true)
            .Concat(typeof(Gatehouse.Api.Features.Platform.PlatformEndpoints).Assembly.GetTypes()
                .Where(t => t.Namespace?.StartsWith("Gatehouse.Api.Features.Platform", StringComparison.Ordinal) == true))
            .ToList();
        Assert.NotEmpty(types);

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = types
            .SelectMany(t => t.GetConstructors(All).Cast<MethodBase>().Concat(t.GetMethods(All))
                .SelectMany(m => m.GetParameters())
                .Where(p => Forbidden.Any(f => f.IsAssignableFrom(p.ParameterType)))
                .Select(p => $"{t.FullName}.{p.Member.Name}({p.ParameterType.Name})"))
            .ToList();

        Assert.Empty(offenders);
    }
}
