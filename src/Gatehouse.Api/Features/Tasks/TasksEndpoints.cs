using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Credentials.RevealPin;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Application.Tasks.ListStaffTasks;
using Gatehouse.Application.Tasks.StaffTaskActions;

namespace Gatehouse.Api.Features.Tasks;

public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tasks", async (string? status, ListStaffTasksHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListStaffTasksQuery(status ?? "open"), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("ListStaffTasks");

        endpoints.MapPost("/api/v1/tasks/{taskId:guid}/claim", async (Guid taskId, ClaimStaffTaskHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(taskId, ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("ClaimStaffTask");

        endpoints.MapPost("/api/v1/tasks/{taskId:guid}/resolve", async (
            Guid taskId, ResolveStaffTaskDto? body, ResolveStaffTaskHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(taskId, body, ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("ResolveStaffTask");

        endpoints.MapPost("/api/v1/tasks/{taskId:guid}/pin-reveal", async (
            Guid taskId, RevealPinHandler handler, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await handler.HandleAsync(taskId, ct)).ToHttpResult(Results.Ok);
            })
            .RequireAuthorization(Policies.SecurityStaff);

        return endpoints;
    }
}
