using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

/// <summary>A migrated, empty tenant database for mapping tests.</summary>
public sealed class TenantTestDb
{
    private TenantTestDb(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<TenantTestDb> CreateAsync(PostgresFixture postgres, CancellationToken ct)
    {
        var cs = await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("map"), ct);
        var db = new TenantTestDb(cs);
        await using var context = db.NewContext();
        await context.Database.MigrateAsync(ct);
        return db;
    }

    public TenantDbContext NewContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention();
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new TenantDbContext(builder.Options);
    }
}
