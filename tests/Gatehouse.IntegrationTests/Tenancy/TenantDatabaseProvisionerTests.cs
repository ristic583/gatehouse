using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gatehouse.IntegrationTests.Tenancy;

public sealed class TenantDatabaseProvisionerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Creating_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var provisioner = host.Services.GetRequiredService<TenantDatabaseProvisioner>();
        var id = TenantId.Create("twice-" + Guid.NewGuid().ToString("N")[..8]).Value;

        await provisioner.CreateDatabaseIfMissingAsync(id, ct);
        await provisioner.CreateDatabaseIfMissingAsync(id, ct);

        await using var connection = new NpgsqlConnection(provisioner.BuildConnectionString(id));
        await connection.OpenAsync(ct);
        Assert.Equal(id.DatabaseName, connection.Database);
        Assert.Contains("Maximum Pool Size=10", provisioner.BuildConnectionString(id), StringComparison.Ordinal);
    }
}
