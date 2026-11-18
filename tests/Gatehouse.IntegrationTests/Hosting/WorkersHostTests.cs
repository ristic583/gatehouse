extern alias GatehouseWorkers;

using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkersHost = GatehouseWorkers::Gatehouse.Workers.WorkersHost;

namespace Gatehouse.IntegrationTests.Hosting;

/// <summary>Builds the real Workers host with DI validation (M7 follow-up: Workers crashed at start on a missing registration).</summary>
public sealed class WorkersHostTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Workers_host_builds_with_validated_services()
    {
        var catalog = await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("workers_host"), TestContext.Current.CancellationToken);
        var builder = WorkersHost.CreateBuilder(
        [
            $"--ConnectionStrings:catalog={catalog}",
            "--ConnectionStrings:rabbitmq=amqp://guest:guest@localhost:5672",
            "--Gatehouse:Secrets:MasterKey=workers-host-test-master-key-0123456789",
        ]);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        using var host = builder.Build();

        Assert.NotEmpty(host.Services.GetServices<IHostedService>());
    }
}
