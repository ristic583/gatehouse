using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy;

public interface ICatalogReader
{
    public Task<IReadOnlyList<TenantSummary>> GetActiveTenantsAsync(CancellationToken ct);

    public Task<TenantSummary?> FindAsync(TenantId id, CancellationToken ct);
}
