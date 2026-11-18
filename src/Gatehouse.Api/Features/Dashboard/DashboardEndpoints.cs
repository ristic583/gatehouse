using Gatehouse.Application.Authorization;
using Gatehouse.Application.Dashboard;

namespace Gatehouse.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/dashboard", async (GetDashboardHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("GetDashboard");
        return endpoints;
    }
}
