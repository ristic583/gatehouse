using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform;

/// <summary>The catalog tables platform handlers may use (ADR-009 §2: platform routes are catalog-only).</summary>
public interface IPlatformCatalogDb
{
    public DbSet<Tenant> Tenants { get; }

    public DbSet<TenantProvisioning> TenantProvisionings { get; }

    public DbSet<TenantStatistics> TenantStatistics { get; }

    public DbSet<ConnectorRegistration> ConnectorRegistrations { get; }

    public DbSet<PlatformAuditEntry> PlatformAuditEntries { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
