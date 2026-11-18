using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Tenancy;

public interface ITenantConnectionResolver
{
    public Task<Result<string>> ResolveAsync(TenantId tenantId, CancellationToken ct);
}
