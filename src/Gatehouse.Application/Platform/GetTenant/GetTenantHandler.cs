using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Platform.GetTenant;

public sealed class GetTenantHandler(PlatformTenantViews views)
{
    public async Task<Result<PlatformTenantDto>> HandleAsync(string tenantId, CancellationToken ct)
    {
        var id = PlatformErrors.ParseTenantId(tenantId);
        return id.IsFailure ? id.Error : await views.GetAsync(id.Value, ct);
    }
}
