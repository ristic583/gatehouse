using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gatehouse.IntegrationTests.Access.AccessRequestEndpointTests;

namespace Gatehouse.IntegrationTests.Access;

public sealed class ApprovalRoutingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Custom_matrix_in_one_tenant_does_not_affect_another()
    {
        await SetBricksLowChainAsync(ApproverChain.AreaOwner);
        try
        {
            using var anna = _factory.CreateClientFor(DemoUser.Anna);
            using var response = await PostRequestAsync(_factory, anna, "nordlys", "CPH-OFFICE-0");
            var request = await CreatedAsync(response);
            Assert.Equal("Manager", Assert.Single(request.Steps).ApproverKind);

            var maxId = await PersonIdAsync(_factory, "bricks", DemoUser.Max.Email);
            using var tom = _factory.CreateClientFor(DemoUser.Max);
            var tomId = await PersonIdAsync(_factory, "bricks", DemoUser.Tom.Email);
            using var bricks = await PostRequestAsync(_factory, tom, "bricks", "BER-OFFICE", tomId);
            var bricksRequest = await CreatedAsync(bricks);
            Assert.NotEqual(Guid.Empty, maxId);
            Assert.Equal("AreaOwner", Assert.Single(bricksRequest.Steps).ApproverKind);
        }
        finally
        {
            await SetBricksLowChainAsync(ApproverChain.Manager);
        }
    }

    private async Task SetBricksLowChainAsync(ApproverChain chain)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("bricks");
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SecurityLevels
            .Where(l => l.Name == "Low")
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.ApproverChain, chain), TestContext.Current.CancellationToken);
    }
}
