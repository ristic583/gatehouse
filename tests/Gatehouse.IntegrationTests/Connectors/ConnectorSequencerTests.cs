using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ConnectorSequencerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Concurrent_inserts_get_distinct_increasing_sequences()
    {
        var ct = TestContext.Current.CancellationToken;
        PacsInstanceId instance;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            instance = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().FirstAsync(ct)).Id;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            return await scope.ServiceProvider.GetRequiredService<IConnectorSequencer>().NextAsync(instance, "person:concurrent", ct);
        }));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), results.Order());
    }
}
