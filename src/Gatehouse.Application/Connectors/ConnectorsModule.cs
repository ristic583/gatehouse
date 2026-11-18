using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Connectors;

public static class ConnectorsModule
{
    public static IServiceCollection AddConnectorsModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IConnectorCommandSignal, InMemoryConnectorCommandSignal>();
        services.AddScoped<ConnectorCommandFactory>();
        services.AddScoped<DispatchFulfillmentHandler>();
        services.TryAddScoped<Fulfillment.PacsAdmin.GrantMigrationService>();
        services.AddScoped<SupersedeFulfillmentCommandsHandler>();
        services.AddScoped<ConnectorContext>();
        services.AddScoped<RecordHeartbeatHandler>();
        services.TryAddScoped<Tasks.StaffTaskService>();
        services.AddScoped<ConnectorCommandDelivery>();
        services.AddScoped<AckCommandsHandler>();
        services.AddScoped<ReceiveDoorEvents.ReceiveDoorEventsHandler>();
        services.AddScoped<ReceiveRecordExport.ReceiveRecordExportHandler>();
        services.TryAddSingleton<IDoorEventDeduplicator, InMemoryDoorEventDeduplicator>();
        services.AddScoped<Fulfillment.ApplyConnectorAckHandler>();
        services.AddScoped<PollCommandsHandler>();
        services.TryAddSingleton<IConnectorPollGate, InMemoryConnectorPollGate>();
        return services;
    }
}
