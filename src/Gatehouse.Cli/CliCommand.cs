using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Cli;

public abstract record CliCommand
{
    public sealed record MigrateAll : CliCommand;

    public sealed record MigrateTenant(TenantId TenantId) : CliCommand;

    public sealed record CreateTenant(TenantId TenantId, string DisplayName, IdpKind IdpKind) : CliCommand;

    public sealed record Seed(TenantId TenantId, bool ResetAccess) : CliCommand;

    public sealed record ConnectorCreate(TenantId TenantId, string PacsName) : CliCommand;

    /// <summary><c>Demo</c> false (<c>bootstrap --production</c>): catalog, platform operator, connector CA and tenant migrations
    /// only.</summary>
    public sealed record Bootstrap(bool Demo = true) : CliCommand;

    /// <summary><c>TenantId</c> null: every active tenant. <c>Apply</c> false: list only, change nothing.</summary>
    public sealed record RepairPendingDuplicates(TenantId? TenantId, bool Apply) : CliCommand;

    /// <summary>Development only: <c>Apply</c> false lists the <c>e2e-*</c>/<c>scn-*</c> tenants, true removes them.</summary>
    public sealed record PurgeTestTenants(bool Apply) : CliCommand;

    public sealed record Help : CliCommand;
}
