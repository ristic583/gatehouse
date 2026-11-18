using Gatehouse.Domain.Common;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>Creates tenant databases on the shared Postgres server (ADR-001).</summary>
public sealed class TenantDatabaseProvisioner(IConfiguration configuration)
{
    public const int MaxPoolSize = TenantConnectionPool.MaxPoolSize;

    private string ServerConnectionString => configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' is not configured.");

    public async Task CreateDatabaseIfMissingAsync(TenantId id, CancellationToken ct)
    {
        // The database name comes only from TenantId.DatabaseName, which is validated by TenantId.Create.
        var databaseName = id.DatabaseName;

        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync(ct);

        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync(ct) is not null)
            {
                return;
            }
        }

        await using var create = connection.CreateCommand();
#pragma warning disable CA2100 // Identifier comes from a validated TenantId and is quoted; CREATE DATABASE takes no parameters.
        create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
#pragma warning restore CA2100
        try
        {
            await create.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Created concurrently; the outcome is the same.
        }
    }

    public string BuildConnectionString(TenantId id) =>
        TenantConnectionPool.Apply(
            new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = id.DatabaseName }.ConnectionString);
}
