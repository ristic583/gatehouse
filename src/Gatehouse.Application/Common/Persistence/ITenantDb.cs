using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Common.Persistence;

public interface ITenantDb
{
    public DbSet<TenantSetting> TenantSettings { get; }

    public DbSet<Site> Sites { get; }

    public DbSet<Floor> Floors { get; }

    public DbSet<Area> Areas { get; }

    public DbSet<Door> Doors { get; }

    public DbSet<Reader> Readers { get; }

    public DbSet<SecurityLevel> SecurityLevels { get; }

    public DbSet<Person> Persons { get; }

    public DbSet<RoleAssignment> RoleAssignments { get; }

    public DbSet<Entitlement> Entitlements { get; }

    public DbSet<PacsInstance> PacsInstances { get; }

    public DbSet<AccessLevelMapping> AccessLevelMappings { get; }

    public DbSet<Domain.Fulfillment.Fulfillment> Fulfillments { get; }

    public DbSet<Domain.Credentials.Card> Cards { get; }

    public DbSet<Domain.Credentials.PinCredential> PinCredentials { get; }

    public DbSet<Domain.Credentials.Photo> Photos { get; }

    public DbSet<Domain.Identity.MagicLink> MagicLinks { get; }

    public DbSet<Domain.Connectors.ConnectorCommand> ConnectorCommands { get; }

    public DbSet<StaffTask> StaffTasks { get; }

    public DbSet<Domain.LiveBuilding.DoorEvent> DoorEvents { get; }

    public DbSet<Domain.LiveBuilding.Alarm> Alarms { get; }

    public DbSet<Domain.LiveBuilding.DoorCommand> DoorCommands { get; }

    public DbSet<AuditEntry> AuditEntries { get; }

    public DbSet<Domain.Reconciliation.ReconciliationReport> ReconciliationReports { get; }

    public DbSet<Domain.Reconciliation.ReconciliationFinding> ReconciliationFindings { get; }

    public DbSet<Domain.Reconciliation.ReconciliationExportRecord> ReconciliationExportRecords { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>Whether a failed save was a unique violation of exactly <paramref name="constraintName"/> (index or constraint).</summary>
    public bool IsUniqueViolation(DbUpdateException exception, string constraintName);
}
