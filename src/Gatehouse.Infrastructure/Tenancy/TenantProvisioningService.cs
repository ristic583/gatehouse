using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Tenancy;

public sealed class TenantProvisioningService(
    CatalogDbContext catalog,
    TenantDatabaseProvisioner provisioner,
    ISecretProtector protector,
    MigrationRunner migrations,
    TimeProvider timeProvider,
    ILogger<TenantProvisioningService> logger)
{
    public async Task<Result> CreateTenantAsync(TenantId id, string displayName, IdpKind idp, CancellationToken ct)
    {
        if (await catalog.Tenants.AnyAsync(t => t.Id == id, ct))
        {
            return Error.Conflict("tenant.already_exists", $"Tenant '{id}' already exists.");
        }

        var encrypted = await EnsureDatabaseAsync(id, ct);

        var tenant = Domain.Tenancy.Tenant.Create(id, displayName, idp, encrypted, timeProvider.GetUtcNow());
        if (tenant.IsFailure)
        {
            return tenant.Error;
        }

        catalog.Tenants.Add(tenant.Value);
        await catalog.SaveChangesAsync(ct);
        var slug = id.Value;
        var databaseName = tenant.Value.DatabaseName;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Tenant {TenantId} created with database {DatabaseName}", slug, databaseName);
        }

        var migrated = await migrations.MigrateTenantAsync(id, ct);
        return migrated.Succeeded
            ? Result.Success()
            : Error.Failure("tenant.migration_failed", migrated.Error ?? "Tenant migration failed.");
    }

    /// <summary>Creates the tenant database if missing and returns its protected connection string (shared with the M10 provisioning
    /// runner).</summary>
    public async Task<string> EnsureDatabaseAsync(TenantId id, CancellationToken ct)
    {
        await provisioner.CreateDatabaseIfMissingAsync(id, ct);
        return protector.Protect(provisioner.BuildConnectionString(id));
    }

    /// <summary>
    /// Creates the tenant unless it already exists. Returns true when it was created.
    /// An existing tenant's stored connection string is re-pointed at the configured server, so a demo catalog
    /// survives a changed Postgres host port (e.g. a run with randomized ports).
    /// </summary>
    public async Task<Result<bool>> CreateTenantIfMissingAsync(TenantId id, string displayName, IdpKind idp, CancellationToken ct)
    {
        var result = await CreateTenantAsync(id, displayName, idp, ct);
        if (result.IsSuccess)
        {
            return true;
        }

        if (result.Error.Code != "tenant.already_exists")
        {
            return result.Error;
        }

        var encrypted = protector.Protect(provisioner.BuildConnectionString(id));
        await catalog.Tenants.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.EncryptedConnectionString, encrypted), ct);
        return false;
    }
}
