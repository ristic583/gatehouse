using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Persistence;

/// <summary>Applies catalog and tenant migrations (ADR-001). One failing tenant does not stop the others.</summary>
public sealed class MigrationRunner(
    IServiceScopeFactory scopeFactory,
    ISecretProtector protector,
    ITenantLoop tenantLoop,
    ILogger<MigrationRunner> logger,
    TimeProvider? timeProvider = null)
{
    public async Task MigrateCatalogAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(ct);
        logger.LogInformation("Catalog database migrated");
    }

    public async Task<TenantMigrationResult> MigrateTenantAsync(TenantId tenantId, CancellationToken ct)
    {
        try
        {
            string? encrypted;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                encrypted = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == tenantId)
                    .Select(t => t.EncryptedConnectionString)
                    .SingleOrDefaultAsync(ct);
            }

            if (encrypted is null)
            {
                return new TenantMigrationResult(tenantId, false, "tenant.unknown");
            }

            await using var db = CreateTenantDb(Tenancy.TenantConnectionPool.Apply(tenantId, protector.Unprotect(encrypted)));
            await db.Database.MigrateAsync(ct);
            await LiveBuilding.DoorEventPartitions.EnsureAsync(
                db, DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime), 8, ct);
            var slug = tenantId.Value;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Tenant database migrated for {TenantId}", slug);
            }
            return new TenantMigrationResult(tenantId, true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Host boundary for one tenant: report and continue with the next tenant.
            var failedSlug = tenantId.Value;
            logger.LogError(ex, "Tenant database migration failed for {TenantId}", failedSlug);
            return new TenantMigrationResult(tenantId, false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Migrates the catalog, then every tenant that is not deprovisioned (bounded, <see cref="ITenantLoop"/>).</summary>
    public async Task<IReadOnlyList<TenantMigrationResult>> MigrateAllAsync(CancellationToken ct)
    {
        await MigrateCatalogAsync(ct);

        List<TenantId> tenantIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants
                .AsNoTracking()
                .Where(t => t.Status != TenantStatus.Deprovisioned)
                .OrderBy(t => t.Id)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        var results = new TenantMigrationResult[tenantIds.Count];
        await tenantLoop.ForEachAsync(
            tenantIds, async (tenantId, token) => results[tenantIds.IndexOf(tenantId)] = await MigrateTenantAsync(tenantId, token), ct);
        return results;
    }

    internal static TenantDbContext CreateTenantDb(string connectionString) =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(Tenancy.TenantConnectionPool.Apply(connectionString))
            .UseSnakeCaseNamingConvention()
            .Options);
}
