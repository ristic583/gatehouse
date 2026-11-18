using Gatehouse.Application.Connectors;
using Gatehouse.Application.Connectors.ReceiveDoorEvents;
using Gatehouse.Application.Connectors.ReceiveRecordExport;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Infrastructure.Connectors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gatehouse.ConnectorGateway;

public static class ConnectorEndpoints
{
    public static RouteGroupBuilder MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/connector/v1").AddEndpointFilter<ContractVersionFilter>();

        group.MapPost("/enroll", async (EnrollRequest request, EnrollConnectorService service, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.EnrollmentToken) || string.IsNullOrWhiteSpace(request.CsrPem))
            {
                return ConnectorProblems.Create(
                    http, "connector.validation_failed", 400, "Validation failed", "enrollmentToken and csrPem are required.");
            }

            var result = await service.EnrollAsync(request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ConnectorProblems.FromError(http, result.Error);
        }).AllowAnonymous();

        var secured = group.MapGroup(string.Empty).RequireAuthorization();

        secured.MapPost("/heartbeat", async (Heartbeat heartbeat, RecordHeartbeatHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RecordHeartbeatCommand(heartbeat), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ConnectorProblems.FromError(http, result.Error);
        });

        secured.MapPost("/poll", async (
            PollRequest? request, PollCommandsHandler service, ConnectorContext connector, HttpContext http, CancellationToken ct) =>
        {
            var maxBatch = request?.MaxBatch ?? 50;
            var wait = request?.WaitSeconds ?? 25;
            if (maxBatch is < 1 or > 100 || wait is < 0 or > 30)
            {
                return ConnectorProblems.Create(
                    http, "connector.validation_failed", 400, "Validation failed", "maxBatch must be 1-100 and waitSeconds 0-30.");
            }

            connector.ClientCertificate = http.Connection.ClientCertificate;
            var result = await service.HandleAsync(new PollCommandsCommand(connector, maxBatch, wait), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ConnectorProblems.FromError(http, result.Error);
        });

        secured.MapPost("/acks", async (AckRequest request, AckCommandsHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : ConnectorProblems.FromError(http, result.Error);
        });

        secured.MapPost("/events", async (
            DoorEventsRequest request, ReceiveDoorEventsHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status202Accepted)
                : ConnectorProblems.FromError(http, result.Error);
        });

        secured.MapPost("/exports", async (
            RecordExport export, ReceiveRecordExportHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(export, ct);
            return result.IsSuccess
                ? Results.Json(result.Value, statusCode: StatusCodes.Status202Accepted)
                : ConnectorProblems.FromError(http, result.Error);
        });

        return group;
    }
}
