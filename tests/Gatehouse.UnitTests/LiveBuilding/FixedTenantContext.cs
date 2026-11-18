using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.LiveBuilding;

internal sealed class FixedTenantContext(string tenantId) : ITenantContext
{
    public TenantId TenantId { get; } = new(tenantId);

    public bool IsResolved => true;
}
