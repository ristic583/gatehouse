using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>Binds the current scope to a tenant. Implemented by the scoped <see cref="TenantContext"/>.</summary>
public interface ITenantContextSetter
{
#pragma warning disable CA1716 // `Set` is a VB keyword; the C#-only interface keeps the name fixed by the M1-09 plan.
    public void Set(TenantId id, string connectionString);
#pragma warning restore CA1716
}
