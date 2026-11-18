using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Persistence.Catalog;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Domain.Tenancy.Tenant> Tenants => Set<Domain.Tenancy.Tenant>();

    public DbSet<ConnectorRegistration> ConnectorRegistrations => Set<ConnectorRegistration>();

    public DbSet<ConnectorAuthority> ConnectorAuthorities => Set<ConnectorAuthority>();

    public DbSet<TenantProvisioning> TenantProvisionings => Set<TenantProvisioning>();

    public DbSet<PlatformOperator> PlatformOperators => Set<PlatformOperator>();

    public DbSet<PlatformAuditEntry> PlatformAuditEntries => Set<PlatformAuditEntry>();

    public DbSet<TenantStatistics> TenantStatistics => Set<TenantStatistics>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly,
            t => t.Namespace == typeof(Configurations.TenantConfiguration).Namespace);
    }
}
