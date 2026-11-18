using Gatehouse.Application.Platform;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>Catalog-only view for platform handlers; shares the scoped <see cref="CatalogDbContext"/> with <see
/// cref="PlatformAuditLog"/>.</summary>
public sealed class PlatformCatalogDb(CatalogDbContext db) : IPlatformCatalogDb
{
    public DbSet<Domain.Tenancy.Tenant> Tenants => db.Tenants;

    public DbSet<TenantProvisioning> TenantProvisionings => db.TenantProvisionings;

    public DbSet<TenantStatistics> TenantStatistics => db.TenantStatistics;

    public DbSet<ConnectorRegistration> ConnectorRegistrations => db.ConnectorRegistrations;

    public DbSet<PlatformAuditEntry> PlatformAuditEntries => db.PlatformAuditEntries;

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
