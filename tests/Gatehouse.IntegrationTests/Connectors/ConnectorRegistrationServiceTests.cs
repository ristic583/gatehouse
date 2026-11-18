using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ConnectorRegistrationServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.ExecuteDeleteAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Create_returns_token_and_sets_connector_mode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>().CreateAsync(new TenantId("nordlys"), "HQ-Copenhagen", ct);
        Assert.True(result.IsSuccess);
        Assert.StartsWith("ghe_", result.Value, StringComparison.Ordinal);
        var registration = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.AsNoTracking().SingleAsync(ct);
        Assert.Equal(ConnectorStatus.PendingEnrollment, registration.Status);
        Assert.Equal(ConnectorRegistrationService.HashToken(result.Value), registration.EnrollmentTokenHash);
        await using var tenantScope = await _factory.CreateTenantScopeAsync("nordlys");
        var instance = await tenantScope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
        Assert.Equal(FulfillmentMode.Connector, instance.FulfillmentMode);
    }

    [Fact]
    public async Task Second_registration_conflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>();
        Assert.True((await service.CreateAsync(new TenantId("nordlys"), "HQ-Copenhagen", ct)).IsSuccess);
        var second = await service.CreateAsync(new TenantId("nordlys"), "HQ-Copenhagen", ct);
        Assert.Equal("connector.already_exists", second.Error.Code);
        var missing = await service.CreateAsync(new TenantId("nordlys"), "No-Such-Pacs", ct);
        Assert.Equal("connector.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Demo_bootstrap_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>();
        await service.EnsureDemoConnectorAsync("ghe_demo-token", ConnectorRegistrationService.DemoPacs, ct);
        await service.EnsureDemoConnectorAsync("ghe_demo-token", ConnectorRegistrationService.DemoPacs, ct);
        var all = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.AsNoTracking().ToListAsync(ct);
        var only = Assert.Single(all);
        Assert.Equal(ConnectorRegistrationService.HashToken("ghe_demo-token"), only.EnrollmentTokenHash);
    }

    [Fact]
    public async Task Demo_connector_is_created_per_pacs_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>();
        await service.EnsureDemoConnectorAsync("ghe_demo-cph", ConnectorRegistrationService.DemoPacs, ct);
        await service.EnsureDemoConnectorAsync("ghe_demo-aar", ConnectorRegistrationService.DemoPacsAarhus, ct);
        await service.EnsureDemoConnectorAsync("ghe_demo-aar", ConnectorRegistrationService.DemoPacsAarhus, ct);

        var all = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.AsNoTracking().ToListAsync(ct);
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Select(r => r.PacsInstanceId).Distinct().Count());
        Assert.Contains(all, r => r.EnrollmentTokenHash == ConnectorRegistrationService.HashToken("ghe_demo-aar"));
        Assert.Contains(all, r => r.EnrollmentTokenHash == ConnectorRegistrationService.HashToken("ghe_demo-cph"));
    }
}
