using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.FulfillmentActions;
using Gatehouse.Application.Fulfillment.GetFulfillment;
using Gatehouse.Application.Fulfillment.ListFulfillments;

namespace Gatehouse.Api.Features.Fulfillment;

public static class FulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapFulfillmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/fulfillments", async (string? view, ListFulfillmentsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListFulfillmentsQuery(view ?? "open"), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("ListFulfillments");

        endpoints.MapGet("/api/v1/fulfillments/{fulfillmentId:guid}",
            async (Guid fulfillmentId, GetFulfillmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetFulfillmentQuery(fulfillmentId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("GetFulfillment");

        endpoints.MapPost("/api/v1/fulfillments/{fulfillmentId:guid}/start",
            async (Guid fulfillmentId, StartFulfillmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new StartFulfillmentCommand(fulfillmentId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("StartFulfillment");

        endpoints.MapPost("/api/v1/fulfillments/{fulfillmentId:guid}/confirm",
            async (Guid fulfillmentId, ConfirmFulfillmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ConfirmFulfillmentCommand(fulfillmentId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("ConfirmFulfillment");

        endpoints.MapPost("/api/v1/fulfillments/{fulfillmentId:guid}/fail",
            async (Guid fulfillmentId, FailFulfillmentDto body, FailFulfillmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new FailFulfillmentCommand(fulfillmentId, body.Reason), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("FailFulfillment");

        endpoints.MapPost("/api/v1/fulfillments/{fulfillmentId:guid}/retry",
            async (Guid fulfillmentId, RetryFulfillmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RetryFulfillmentCommand(fulfillmentId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("RetryFulfillment");

        return endpoints;
    }
}
