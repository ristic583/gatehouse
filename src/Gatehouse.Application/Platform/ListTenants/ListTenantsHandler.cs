using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;

namespace Gatehouse.Application.Platform.ListTenants;

public sealed class ListTenantsHandler(PlatformTenantViews views)
{
    public Task<IReadOnlyList<PlatformTenantSummaryDto>> HandleAsync(CancellationToken ct) => views.ListAsync(ct);
}
