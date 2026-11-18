using Gatehouse.Infrastructure.Messaging.Consumers;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging;

public static class GatehouseConsumers
{
    /// <summary>Registers all Workers consumers plus tenant scoping and retry on every endpoint.</summary>
    public static void AddGatehouseConsumers(this IBusRegistrationConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        configurator.AddConsumer<FulfillmentFailedTaskConsumer>();
        configurator.AddConsumer<FulfillmentConfirmedTaskConsumer>();
        configurator.AddConsumer<FulfillmentAckConsumer>();
        configurator.AddConsumer<FulfillmentDesiredConsumer>();
        configurator.AddConsumer<FulfillmentSupersededConsumer>();
        configurator.AddConsumer<DoorEventIngestConsumer>();
        configurator.AddConsumer<ConnectorHealthConsumer>();
        configurator.AddConsumer<ReconciliationExportConsumer>();
        configurator.AddConsumer<ReconciliationExportAckConsumer>();
        configurator.AddConsumer<CardCommandAckConsumer>();
        configurator.AddConsumer<FindingRemovalAckConsumer>();
        configurator.AddConsumer<ApprovalNotificationConsumer>();
        configurator.AddConfigureEndpointsCallback((ctx, _, cfg) =>
        {
            cfg.UseMessageRetry(r =>
            {
                r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
                r.Ignore<MissingTenantHeaderException>();
            });
            cfg.UseConsumeFilter(typeof(TenantScopeConsumeFilter<>), ctx);
        });
    }
}
