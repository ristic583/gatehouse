using Gatehouse.Application.Identity;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Identity;

public sealed class RoleResolverTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Manager_role_is_derived_from_reports()
    {
        Assert.Contains("Manager", await RolesOfAsync("mikkel.holm@nordlys.test"));
        Assert.DoesNotContain("Manager", await RolesOfAsync("person-01@nordlys.test"));
    }

    [Fact]
    public async Task Area_owner_role_is_derived_from_owned_areas()
    {
        Assert.Contains("AreaOwner", await RolesOfAsync("sofie.berg@nordlys.test"));
        Assert.DoesNotContain("AreaOwner", await RolesOfAsync("mikkel.holm@nordlys.test"));
    }

    [Fact]
    public async Task Assigned_roles_are_included()
    {
        Assert.Contains("SecurityStaff", await RolesOfAsync("jonas.nielsen@nordlys.test"));
        var freja = await RolesOfAsync("freja.dahl@nordlys.test");
        Assert.Contains("TenantAdmin", freja);
        Assert.Contains("Employee", freja);
    }

    private async Task<IReadOnlyList<string>> RolesOfAsync(string email)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var id = await db.Persons.Where(p => p.Email == email).Select(p => p.Id).SingleAsync(ct);
        return await scope.ServiceProvider.GetRequiredService<RoleResolver>().GetRolesAsync(id, ct);
    }
}
