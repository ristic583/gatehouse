using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Admin;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant.Conventions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gatehouse.Infrastructure.Persistence.Tenant;

public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options), ITenantDb
{
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Floor> Floors => Set<Floor>();

    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Door> Doors => Set<Door>();

    public DbSet<Reader> Readers => Set<Reader>();

    public DbSet<SecurityLevel> SecurityLevels => Set<SecurityLevel>();

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    public DbSet<PacsInstance> PacsInstances => Set<PacsInstance>();

    public DbSet<AccessLevelMapping> AccessLevelMappings => Set<AccessLevelMapping>();

    public DbSet<Domain.Fulfillment.Fulfillment> Fulfillments => Set<Domain.Fulfillment.Fulfillment>();

    public DbSet<Domain.Credentials.Card> Cards => Set<Domain.Credentials.Card>();

    public DbSet<Domain.Credentials.PinCredential> PinCredentials => Set<Domain.Credentials.PinCredential>();

    public DbSet<Domain.Credentials.Photo> Photos => Set<Domain.Credentials.Photo>();

    public DbSet<Domain.Identity.MagicLink> MagicLinks => Set<Domain.Identity.MagicLink>();

    public DbSet<Domain.Connectors.ConnectorCommand> ConnectorCommands => Set<Domain.Connectors.ConnectorCommand>();

    public DbSet<StaffTask> StaffTasks => Set<StaffTask>();

    public DbSet<Domain.LiveBuilding.DoorEvent> DoorEvents => Set<Domain.LiveBuilding.DoorEvent>();

    public DbSet<Domain.LiveBuilding.Alarm> Alarms => Set<Domain.LiveBuilding.Alarm>();

    public DbSet<Domain.LiveBuilding.DoorCommand> DoorCommands => Set<Domain.LiveBuilding.DoorCommand>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Domain.Reconciliation.ReconciliationReport> ReconciliationReports => Set<Domain.Reconciliation.ReconciliationReport>();

    public DbSet<Domain.Reconciliation.ReconciliationFinding> ReconciliationFindings => Set<Domain.Reconciliation.ReconciliationFinding>();

    public DbSet<Domain.Reconciliation.ReconciliationExportRecord> ReconciliationExportRecords =>
        Set<Domain.Reconciliation.ReconciliationExportRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (PendingAuditEntries().Count > 0)
        {
            throw new InvalidOperationException("Pending audit entries must be saved with SaveChangesAsync.");
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = PendingAuditEntries();
        var removedAdmins = RemovedTenantAdminIds();
        if (pending.Count == 0 && removedAdmins.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var ownTransaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7340001)", cancellationToken);
            if (removedAdmins.Count > 0
                && !await RoleAssignments.AsNoTracking()
                .AnyAsync(r => r.Role == AssignedRole.TenantAdmin && !removedAdmins.Contains(r.Id), cancellationToken))
            {
                // Re-checked under the tenant lock with committed data: two concurrent removals cannot both pass (GH-AUTH-05).
                throw new LastTenantAdminException();
            }

            var last = await AuditEntries
                .AsNoTracking()
                .OrderByDescending(a => a.Sequence)
                .Select(a => new { a.Sequence, a.Hash })
                .FirstOrDefaultAsync(cancellationToken);
            var sequence = last?.Sequence ?? 0;
            var prevHash = last?.Hash ?? AuditHasher.GenesisHash;
            foreach (var entry in pending)
            {
                entry.Seal(++sequence, prevHash);
                prevHash = entry.Hash;
            }

            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }
    }

    public bool IsUniqueViolation(DbUpdateException exception, string constraintName) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddStronglyTypedIdConverters();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TenantDbContext).Assembly,
            t => t.Namespace == typeof(Configurations.TenantSettingConfiguration).Namespace);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedMessageConfiguration());
    }

    private List<RoleAssignmentId> RemovedTenantAdminIds() =>
        ChangeTracker.Entries<RoleAssignment>()
            .Where(e => e.State == EntityState.Deleted && e.Entity.Role == AssignedRole.TenantAdmin)
            .Select(e => e.Entity.Id)
            .ToList();

    private List<AuditEntry> PendingAuditEntries() =>
        ChangeTracker.Entries<AuditEntry>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .OrderBy(e => e.LocalOrder)
            .ToList();
}
