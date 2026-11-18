extern alias GatehouseGateway;

using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// The Connector Gateway against its own copy of the demo databases (<see cref="DemoTemplate"/>), or, with
/// <paramref name="sharedCatalogConnectionString"/>, against the catalog (and tenant databases) of an API factory.
/// </summary>
public sealed class GatewayFactory(PostgresFixture postgres, string? sharedCatalogConnectionString = null) : WebApplicationFactory<GatehouseGateway::Gatehouse.ConnectorGateway.GatewayProgram>, IAsyncLifetime
{
    private readonly List<string> _databases = [];
    private string _catalogConnectionString = sharedCatalogConnectionString ?? string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (sharedCatalogConnectionString is not null)
        {
            return; // catalog, tenants and seed data belong to the API factory that owns them
        }

        var template = await DemoTemplate.GetAsync(postgres);
        _catalogConnectionString = await template.CloneAsync(postgres, _databases, CancellationToken.None);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var database in _databases)
        {
            await postgres.DropDatabaseAsync(database);
        }

        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:postgres", postgres.ServerConnectionString);
        builder.UseSetting("ConnectionStrings:catalog", _catalogConnectionString);
        builder.UseSetting(AesGcmSecretProtector.MasterKeySetting, GatehouseApiFactory.MasterKey);
        builder.ConfigureServices(services => services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, TestClientCertificateStartupFilter>());
    }
}
