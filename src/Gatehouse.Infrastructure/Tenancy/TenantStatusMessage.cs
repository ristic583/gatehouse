using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>Payload on <c>Tenancy:StatusChannel</c>; <c>Version</c> is the publish time in Unix milliseconds (diagnostics only).</summary>
public sealed record TenantStatusMessage(string TenantId, TenantStatus Status, long Version);
