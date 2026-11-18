using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Persistence;

/// <summary>Outcome of migrating one tenant database (<see cref="MigrationRunner"/>).</summary>
public sealed record TenantMigrationResult(TenantId TenantId, bool Succeeded, string? Error);
