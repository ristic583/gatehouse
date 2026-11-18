using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Tenancy;

public sealed class TenantDbContextTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Each_tenant_scope_connects_to_its_own_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var alpha = await host.AddTenantAsync("alpha", TenantStatus.Active, ct);
        var beta = await host.AddTenantAsync("beta", TenantStatus.Active, ct);
        var scopes = host.Services.GetRequiredService<ITenantScopeFactory>();
        var key = "isolation-" + Guid.NewGuid().ToString("N");

        await using (var alphaScope = await scopes.CreateScopeAsync(alpha, ct))
        {
            var db = alphaScope.ServiceProvider.GetRequiredService<ITenantDb>();
            db.TenantSettings.Add(new TenantSetting(key, "alpha-only", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);

            Assert.Equal(1, await db.TenantSettings.CountAsync(s => s.Key == key, ct));
            Assert.Equal(alpha, alphaScope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId);
        }

        await using (var betaScope = await scopes.CreateScopeAsync(beta, ct))
        {
            var db = betaScope.ServiceProvider.GetRequiredService<ITenantDb>();

            Assert.Equal(0, await db.TenantSettings.CountAsync(s => s.Key == key, ct));
            Assert.Equal(0, await db.TenantSettings.CountAsync(ct));
        }
    }

    [Fact]
    public async Task Resolving_db_without_tenant_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        await using var scope = host.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        Assert.False(context.IsResolved);
        Assert.Throws<TenantNotResolvedException>(() => context.TenantId);
        Assert.Throws<TenantNotResolvedException>(() => scope.ServiceProvider.GetRequiredService<ITenantDb>());
    }
}
