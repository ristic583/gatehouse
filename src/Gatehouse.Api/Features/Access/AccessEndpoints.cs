using FluentValidation;
using Gatehouse.Api.Http;
using Gatehouse.Application.Access.AccessMap;
using Gatehouse.Application.Access.Approvals;
using Gatehouse.Application.Access.AreaRoster;
using Gatehouse.Application.Access.CancelAccessRequest;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Access.ListAccessRequests;
using Gatehouse.Application.Access.RequestAccess;
using Gatehouse.Application.Access.Revocation;
using Gatehouse.Application.Authorization;

namespace Gatehouse.Api.Features.Access;

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/floors/{floorId:guid}/access-map", async (
            Guid floorId, GetFloorAccessMapHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetFloorAccessMapQuery(floorId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetFloorAccessMap");

        endpoints.MapPost("/api/v1/access-requests", CreateAccessRequestAsync)
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("CreateAccessRequest");

        endpoints.MapGet("/api/v1/access-requests", async (ListAccessRequestsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListAccessRequestsQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ListAccessRequests");

        endpoints.MapGet("/api/v1/access-requests/{entitlementId:guid}", async (
            Guid entitlementId, GetAccessRequestHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetAccessRequestQuery(entitlementId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetAccessRequest");

        endpoints.MapGet("/api/v1/approvals/inbox", async (GetApprovalInboxHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetApprovalInboxQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetApprovalInbox");

        endpoints.MapPost("/api/v1/approvals/{stepId:guid}/approve", async (
            Guid stepId, DecideStepDto? body, ApproveStepHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DecideStepCommand(stepId, body?.Comment), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ApproveStep");

        endpoints.MapPost("/api/v1/approvals/{stepId:guid}/reject", async (
            Guid stepId, DecideStepDto? body, RejectStepHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DecideStepCommand(stepId, body?.Comment), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("RejectStep");

        endpoints.MapPost("/api/v1/access-requests/{entitlementId:guid}/cancel", async (
            Guid entitlementId, CancelAccessRequestHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CancelAccessRequestCommand(entitlementId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("CancelAccessRequest");

        endpoints.MapPost("/api/v1/access-requests/{entitlementId:guid}/revoke", async (
            Guid entitlementId, RevokeEntitlementDto? body, RevokeEntitlementHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RevokeEntitlementCommand(entitlementId, body?.Reason), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("RevokeEntitlement");

        endpoints.MapPost("/api/v1/access-requests/{entitlementId:guid}/emergency-revoke", async (
            Guid entitlementId, RevokeEntitlementDto? body, EmergencyRevokeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RevokeEntitlementCommand(entitlementId, body?.Reason), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("EmergencyRevokeEntitlement");

        endpoints.MapGet("/api/v1/areas/{areaId:guid}/roster", async (Guid areaId, GetAreaRosterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetAreaRosterQuery(areaId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetAreaRoster");

        return endpoints;
    }

    private static async Task<IResult> CreateAccessRequestAsync(
        CreateAccessRequestDto request,
        IValidator<RequestAccessCommand> validator,
        RequestAccessHandler handler,
        CancellationToken ct)
    {
        var command = new RequestAccessCommand(request);
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
        {
            return validation.ToDictionary().ToValidationProblem();
        }

        return (await handler.HandleAsync(command, ct))
            .ToHttpResult(dto => Results.Created($"/api/v1/access-requests/{dto.EntitlementId}", dto));
    }
}
