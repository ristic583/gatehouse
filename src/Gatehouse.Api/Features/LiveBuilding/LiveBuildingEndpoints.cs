using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.LiveBuilding.GetLiveFloor;
using Gatehouse.Application.LiveBuilding.ListAlarms;
using Gatehouse.Application.LiveBuilding.RequestDoorCommand;

namespace Gatehouse.Api.Features.LiveBuilding;

public static class LiveBuildingEndpoints
{
    public static IEndpointRouteBuilder MapLiveBuildingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/live/connectors", async (Guid? siteId, GetConnectorHealthHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetConnectorHealthQuery(siteId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("ListLiveConnectors");

        endpoints.MapGet("/api/v1/live/floors/{floorId:guid}", async (Guid floorId, GetLiveFloorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetLiveFloorQuery(floorId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("GetLiveFloor");

        endpoints.MapGet("/api/v1/live/floors/{floorId:guid}/events",
                async (
                    Guid floorId, DateTimeOffset? from, DateTimeOffset? to,
                    Gatehouse.Application.LiveBuilding.Replay.FloorEventsHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(floorId, from, to, ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("GetFloorEvents");

        endpoints.MapPost("/api/v1/live/door-commands", RequestDoorCommandAsync)
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("RequestDoorCommand");

        endpoints.MapGet("/api/v1/live/alarms", async (string? status, Guid? siteId, ListAlarmsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListAlarmsQuery(status, siteId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("ListAlarms");

        endpoints.MapPost("/api/v1/live/alarms/{alarmId:guid}/acknowledge", async (
            Guid alarmId, AcknowledgeAlarmRequest request, AcknowledgeAlarmHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new AcknowledgeAlarmCommand(alarmId, request.Comment), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.SecurityStaff)
            .WithName("AcknowledgeAlarm");

        return endpoints;
    }

    private static async Task<IResult> RequestDoorCommandAsync(
        RequestDoorCommandCommand command,
        RequestDoorCommandHandler handler,
        CancellationToken ct)
    {
        return (await handler.HandleAsync(command, ct))
            .ToHttpResult(dto => Results.Created($"/api/v1/live/door-commands/{dto.DoorCommandId}", dto));
    }
}
