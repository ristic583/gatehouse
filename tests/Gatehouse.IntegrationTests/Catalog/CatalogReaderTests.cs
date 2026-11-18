using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;

namespace Gatehouse.IntegrationTests.Catalog;

public sealed class CatalogReaderTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Get_active_tenants_excludes_suspended()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CatalogTestDb.CreateMigratedAsync(postgres, ct);

        await using (var db = CatalogTestDb.Open(connectionString))
        {
            var frozen = CatalogTestDb.NewTenant("frozen", "Frozen Ltd");
            frozen.Suspend("test", DateTimeOffset.UtcNow);
            db.Tenants.AddRange(
                CatalogTestDb.NewTenant("nordlys", "Nordlys A/S"),
                CatalogTestDb.NewTenant("bricks", "Bricks GmbH", IdpKind.Saml),
                frozen);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CatalogTestDb.Open(connectionString))
        {
            var reader = new CatalogReader(db);

            var active = await reader.GetActiveTenantsAsync(ct);
            var suspended = await reader.FindAsync(new TenantId("frozen"), ct);

            Assert.Equal(["bricks", "nordlys"], active.Select(t => t.Id.Value).ToArray());
            Assert.NotNull(suspended);
            Assert.Equal(TenantStatus.Suspended, suspended.Status);
        }
    }
}
