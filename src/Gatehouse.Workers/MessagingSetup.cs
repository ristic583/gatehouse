using System.Text.Json.Serialization;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Messaging.Outbox;
using MassTransit;

namespace Gatehouse.Workers;

public static class MessagingSetup
{
    public static IServiceCollection AddGatehouseWorkersMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("gatehouse", false));
            x.AddGatehouseConsumers();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672"));
                cfg.ConfigureJsonSerializerOptions(o =>
                {
                    o.Converters.Add(new JsonStringEnumConverter());
                    o.Converters.Add(new StronglyTypedIdJsonConverterFactory());
                    return o;
                });
                cfg.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
