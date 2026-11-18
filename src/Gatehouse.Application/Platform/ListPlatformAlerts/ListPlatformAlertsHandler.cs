using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;

namespace Gatehouse.Application.Platform.ListPlatformAlerts;

public sealed class ListPlatformAlertsHandler(PlatformTenantViews views)
{
    public Task<IReadOnlyList<PlatformAlertDto>> HandleAsync(CancellationToken ct) => views.AlertsAsync(ct);
}
