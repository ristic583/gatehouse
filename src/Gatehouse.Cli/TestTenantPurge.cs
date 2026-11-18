using Gatehouse.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Gatehouse.Cli;

/// <summary>
/// <c>purge-test-tenants</c> (development only): lists, and with apply removes, the tenants that e2e (<c>e2e-*</c>) and scenario
/// (<c>scn-*</c>) runs create on the local AppHost database. The product has no tenant deletion, so those runs leave their tenants
/// behind; dozens of them make the background loops touch dozens of databases. Removes the catalog rows first (so running
/// processes stop using the tenant), then drops the tenant database. Keycloak organizations of those tenants are left as they are.
/// Refuses unless the environment is Development and the Postgres host is local.
/// </summary>
public sealed class TestTenantPurge(IConfiguration configuration, IHostEnvironment environment, TextWriter output)
{
    public static readonly string[] Prefixes = ["e2e-", "scn-"];

    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1"];

    public static bool IsTestTenant(string tenantId) => Prefixes.Any(p => tenantId.StartsWith(p, StringComparison.Ordinal));

    public async Task<int> RunAsync(bool apply, CancellationToken ct)
    {
        var catalog = configuration.GetConnectionString("catalog");
        var server = configuration.GetConnectionString("postgres");
        if (!environment.IsDevelopment() || catalog is null || server is null || !IsLocal(catalog) || !IsLocal(server))
        {
            await output.WriteLineAsync(
                "FAILED purge-test-tenants runs only with DOTNET_ENVIRONMENT=Development against a local Postgres (catalog and postgres).");
            return CliRunner.OperationFailed;
        }

        await using var connection = new NpgsqlConnection(catalog);
        await connection.OpenAsync(ct);
        var tenants = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT id FROM catalog.tenants WHERE id LIKE 'e2e-%' OR id LIKE 'scn-%' ORDER BY id";
            await using var reader = await list.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tenants.Add(reader.GetString(0));
            }
        }

        await output.WriteLineAsync($"{tenants.Count} test tenants{(apply ? string.Empty : " (list only; --apply removes them)")}");
        foreach (var id in tenants.Where(IsTestTenant))
        {
            await output.WriteLineAsync($"  {id}");
            if (apply)
            {
                await DeleteCatalogRowsAsync(connection, id, ct);
                await DropDatabaseAsync(server, TenantId.Create(id).Value.DatabaseName, ct);
            }
        }

        return CliRunner.Success;
    }

    private static bool IsLocal(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Host is { } host
        && LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private static async Task DeleteCatalogRowsAsync(NpgsqlConnection connection, string id, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var delete = connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = """
            DELETE FROM catalog.tenant_statistics WHERE tenant_id = @id;
            DELETE FROM catalog.tenant_provisioning WHERE tenant_id = @id;
            DELETE FROM catalog.connector_registrations WHERE tenant_id = @id;
            DELETE FROM catalog.tenants WHERE id = @id;
            """;
        delete.Parameters.AddWithValue("id", id);
        await delete.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task DropDatabaseAsync(string server, string databaseName, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync(ct);
        await using var drop = connection.CreateCommand();
#pragma warning disable CA2100 // Identifier from a validated TenantId (e2e-/scn- only), quoted; DROP DATABASE takes no parameters.
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
#pragma warning restore CA2100
        await drop.ExecuteNonQueryAsync(ct);
    }
}
