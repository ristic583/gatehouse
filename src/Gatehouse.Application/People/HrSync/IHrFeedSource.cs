using Gatehouse.Domain.Common;

namespace Gatehouse.Application.People.HrSync;

public interface IHrFeedSource
{
    /// <summary>The tenant's feed, or null when the tenant has none.</summary>
    public Task<IReadOnlyList<HrFeedRecord>?> ReadAsync(TenantId tenantId, CancellationToken ct);
}
