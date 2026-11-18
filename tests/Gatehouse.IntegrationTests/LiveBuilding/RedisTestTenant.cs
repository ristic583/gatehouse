using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;

namespace Gatehouse.IntegrationTests.LiveBuilding;

internal sealed class RedisTestTenant(string tenantId) : ITenantContext
{
    public TenantId TenantId { get; } = new(tenantId);

    public bool IsResolved => true;
}
