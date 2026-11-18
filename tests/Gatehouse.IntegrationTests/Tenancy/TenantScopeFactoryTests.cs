using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Tenancy;

public sealed class TenantScopeFactoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Unknown_tenant_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var scopes = host.Services.GetRequiredService<ITenantScopeFactory>();

        await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => scopes.CreateScopeAsync(TenantId.Create("nobody").Value, ct));
    }

    [Fact]
    public async Task Suspended_tenant_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var frozen = await host.AddTenantAsync("frozen", TenantStatus.Suspended, ct);
        var scopes = host.Services.GetRequiredService<ITenantScopeFactory>();

        await Assert.ThrowsAsync<TenantNotResolvedException>(() => scopes.CreateScopeAsync(frozen, ct));
    }
}
