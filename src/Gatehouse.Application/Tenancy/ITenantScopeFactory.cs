using Gatehouse.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Tenancy;

public interface ITenantScopeFactory
{
    /// <summary>Creates a DI scope bound to <paramref name="tenantId"/>. Throws <see cref="TenantNotResolvedException"/> for an unknown or
    /// inactive tenant.</summary>
    public Task<AsyncServiceScope> CreateScopeAsync(TenantId tenantId, CancellationToken ct);
}
