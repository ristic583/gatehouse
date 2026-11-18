using FluentValidation;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.LiveBuilding.GetLiveFloor;
using Gatehouse.Application.LiveBuilding.IngestDoorEvents;
using Gatehouse.Application.LiveBuilding.ListAlarms;
using Gatehouse.Application.LiveBuilding.LiveDrift;
using Gatehouse.Application.LiveBuilding.RequestDoorCommand;
using Gatehouse.Application.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.LiveBuilding;

public static class LiveBuildingModule
{
    public static IServiceCollection AddLiveBuildingModule(this IServiceCollection services)
    {
        services.AddLiveBuildingWorkerHandlers();
        services.AddScoped<GetLiveFloorHandler>();
        services.AddScoped<LiveSiteAccess>();
        services.AddScoped<Replay.FloorEventsHandler>();
        services.AddScoped<GetConnectorHealthHandler>();
        services.TryAddScoped<ConnectorCommandFactory>();
        services.AddScoped<RequestDoorCommandHandler>();
        services.AddScoped<ListAlarmsHandler>();
        services.AddScoped<AcknowledgeAlarmHandler>();
        services.AddSingleton<IValidator<AcknowledgeAlarmRequest>, AcknowledgeAlarmValidator>();
        services.AddSingleton<IValidator<RequestDoorCommandCommand>, RequestDoorCommandValidator>();
        return services;
    }

    /// <summary>Handlers without a current user (ingest, heartbeat, offline sweep): all Workers needs.</summary>
    public static IServiceCollection AddLiveBuildingWorkerHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<DoorStateReader>();
        services.TryAddScoped<LiveDriftDetector>();
        services.TryAddScoped<StaffTaskService>();
        services.TryAddScoped<IngestDoorEventsHandler>();
        services.TryAddScoped<RecordConnectorHeartbeatHandler>();
        services.TryAddScoped<SweepOfflineConnectorsHandler>();
        return services;
    }
}
