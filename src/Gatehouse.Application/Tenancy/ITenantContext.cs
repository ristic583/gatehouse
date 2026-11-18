using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy;

public interface ITenantContext
{
    /// <summary>The current tenant. Throws <see cref="TenantNotResolvedException"/> when no tenant is resolved.</summary>
    public TenantId TenantId { get; }

    public bool IsResolved { get; }
}
