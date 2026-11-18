using Gatehouse.Cli;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Cli;

/// <summary>
/// Tenant databases migrated before <c>PendingRequestUnique</c> may hold two pending requests for the same person and area.
/// Each test drops the index on its own clone of the demo tenant and adds a newer duplicate of a seeded pending request.
/// </summary>
public sealed class CliRepairPendingDuplicatesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Dry_run_lists_duplicates_without_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (original, duplicate) = await AddDuplicateAsync(ct);

        var (code, text) = await RunAsync(["repair-pending-duplicates", "--tenant", "nordlys"], ct);

        Assert.Equal(CliRunner.Success, code);
        Assert.Contains($"keep {original.Value}", text, StringComparison.Ordinal);
        Assert.Contains($"would cancel {duplicate.Value}", text, StringComparison.Ordinal);
        Assert.Contains("nothing changed", text, StringComparison.Ordinal);
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(2, await db.Entitlements.CountAsync(e => (e.Id == original || e.Id == duplicate) && e.Status == EntitlementStatus.PendingApproval, ct));
    }

    [Fact]
    public async Task Apply_cancels_newer_duplicates_and_audits()
    {
        var ct = TestContext.Current.CancellationToken;
        var (original, duplicate) = await AddDuplicateAsync(ct);

        var (code, text) = await RunAsync(["repair-pending-duplicates", "--apply"], ct);

        Assert.Equal(CliRunner.Success, code);
        Assert.Contains("nordlys: cancelled 1 duplicate", text, StringComparison.Ordinal);
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(EntitlementStatus.PendingApproval, (await db.Entitlements.SingleAsync(e => e.Id == original, ct)).Status);
        Assert.Equal(EntitlementStatus.Cancelled, (await db.Entitlements.SingleAsync(e => e.Id == duplicate, ct)).Status);
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == "entitlement.cancelled" && a.SubjectId == duplicate.Value.ToString(), ct);
        Assert.Equal(AuditActorKind.System, entry.ActorKind);
        Assert.Equal(PendingDuplicateRepair.AuditComponent, entry.ActorId);
        Assert.Contains("duplicate_pending", entry.Payload, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX ux_entitlements_pending_person_area ON entitlements (person_id, area_id) WHERE status = 'PendingApproval'""", ct);
    }

    [Fact]
    public async Task Migrate_reports_duplicates_with_repair_hint()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddDuplicateAsync(ct);
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database.ExecuteSqlRawAsync(
                """DELETE FROM "__EFMigrationsHistory" WHERE migration_id LIKE '%_PendingRequestUnique'""", ct);
        }

        var (code, text) = await RunAsync(["migrate", "--tenant", "nordlys"], ct);

        Assert.Equal(CliRunner.UsageError, code);
        Assert.Contains("repair-pending-duplicates --tenant nordlys", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgresException", text, StringComparison.Ordinal);
    }

    private async Task<(int Code, string Output)> RunAsync(string[] args, CancellationToken ct)
    {
        var command = CliArguments.Parse(args);
        Assert.True(command.IsSuccess);
        await using var output = new StringWriter();
        var code = await new CliRunner(_factory.Services, output).RunAsync(command.Value, ct);
        return (code, output.ToString());
    }

    /// <summary>Drops the unique index and copies one seeded pending request (one minute newer, no approval steps).</summary>
    private async Task<(EntitlementId Original, EntitlementId Duplicate)> AddDuplicateAsync(CancellationToken ct)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var original = await db.Entitlements.AsNoTracking()
            .Where(e => e.Status == EntitlementStatus.PendingApproval)
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstAsync(ct);
        var duplicate = EntitlementId.New();
        await db.Database.ExecuteSqlRawAsync("DROP INDEX ux_entitlements_pending_person_area", ct);
        await db.Database.ExecuteSqlAsync(
            $"""
            CREATE TEMP TABLE dup AS SELECT * FROM entitlements WHERE id = {original.Value};
            UPDATE dup SET id = {duplicate.Value}, created_at = created_at + interval '1 minute';
            INSERT INTO entitlements SELECT * FROM dup;
            DROP TABLE dup;
            """,
            ct);
        return (original, duplicate);
    }
}
