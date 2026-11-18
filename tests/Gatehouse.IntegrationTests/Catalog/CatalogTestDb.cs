using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Catalog;

internal static class CatalogTestDb
{
    public static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    public static async Task<string> CreateMigratedAsync(PostgresFixture postgres, CancellationToken ct)
    {
        var connectionString = await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("catalog"), ct);
        await using var db = Open(connectionString);
        await db.Database.MigrateAsync(ct);
        return connectionString;
    }

    public static CatalogDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public static Tenant NewTenant(string id, string name, IdpKind idp = IdpKind.Oidc) =>
        Tenant.Create(TenantId.Create(id).Value, name, idp, "v1:not-a-real-secret", Now).Value;
}
