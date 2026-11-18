using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gatehouse.IntegrationTests.Catalog;

public sealed class PlatformCatalogMappingTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = CatalogTestDb.Now;

    [Fact]
    public async Task Tenant_provisioning_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);
        var id = new TenantId("acme");

        await using (var db = CatalogTestDb.Open(cs))
        {
            db.Tenants.Add(Tenant.CreateProvisioning(id, "Acme", "Acme.Test", Now).Value);
            var p = TenantProvisioning.Start(id, "a@acme.test", "A", "B", "Europe/Copenhagen", Now);
            p.Advance(Now);
            db.TenantProvisionings.Add(p);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CatalogTestDb.Open(cs))
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == id, ct);
            var p = await db.TenantProvisionings.SingleAsync(x => x.TenantId == id, ct);
            Assert.Equal(TenantStatus.Provisioning, tenant.Status);
            Assert.Equal("acme.test", tenant.EmailDomain);
            Assert.Equal(ProvisioningStep.Migrate, p.Step);
            Assert.Equal("a@acme.test", p.AdminEmail);
        }
    }

    [Fact]
    public async Task Email_domain_is_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);
        await using var db = CatalogTestDb.Open(cs);
        db.Tenants.Add(Tenant.CreateProvisioning(new TenantId("acme"), "Acme", "acme.test", Now).Value);
        db.Tenants.Add(Tenant.CreateProvisioning(new TenantId("acme2"), "Acme 2", "acme.test", Now).Value);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Platform_audit_is_append_only_and_chained()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);
        var actor = new PlatformActor(PlatformActorKind.Operator, "op-1");

        await using (var db = CatalogTestDb.Open(cs))
        {
            var log = new PlatformAuditLog(db, new FakeClock(Now));
            await log.WriteAsync(actor, "platform.tenant_created", new TenantId("acme"), new { domain = "acme.test" }, ct);
            await log.WriteAsync(PlatformActor.System, "platform.tenant_provisioned", new TenantId("acme"), null, ct);
            await db.SaveChangesAsync(ct);
            await log.WriteAsync(actor, "platform.tenant_suspended", null, new { reason = "x" }, ct);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CatalogTestDb.Open(cs))
        {
            var entries = await db.PlatformAuditEntries.OrderBy(e => e.Sequence).ToListAsync(ct);
            Assert.Equal([1L, 2L, 3L], entries.Select(e => e.Sequence));
            Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
            Assert.Equal(entries[1].Hash, entries[2].PreviousHash);
            foreach (var e in entries)
            {
                Assert.Equal(
                    PlatformAuditLog.Compute(e.PreviousHash, e.Sequence, e.OccurredAt, new PlatformActor(e.ActorKind, e.ActorId), e.Action, e.TenantId, Gatehouse.Domain.Audit.AuditCanonicalJson.Canonicalize(e.Data)),
                    e.Hash);
            }
        }

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE catalog.platform_audit_entries SET action = 'x'", conn);
        await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(ct));
    }

    [Fact]
    public async Task Platform_audit_hash_verifies_after_round_trip_with_sub_microsecond_clock()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);
        var clock = new FakeClock(Now.AddTicks(1234567)); // 7 fractional digits; Postgres keeps 6

        await using (var db = CatalogTestDb.Open(cs))
        {
            await new PlatformAuditLog(db, clock).WriteAsync(PlatformActor.System, "platform.tenant_created", new TenantId("acme"), null, ct);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CatalogTestDb.Open(cs))
        {
            var e = await db.PlatformAuditEntries.SingleAsync(ct);
            Assert.Equal(
                PlatformAuditLog.Compute(e.PreviousHash, e.Sequence, e.OccurredAt, new PlatformActor(e.ActorKind, e.ActorId), e.Action, e.TenantId, Gatehouse.Domain.Audit.AuditCanonicalJson.Canonicalize(e.Data)),
                e.Hash);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
