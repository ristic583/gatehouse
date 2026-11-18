extern alias GatehouseApi;
using Gatehouse.Application.Access.Notifications;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Credentials;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.Reconciliation;
using Gatehouse.Application.Tasks;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Messaging.Consumers;
using Gatehouse.Infrastructure.Security;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// The Workers pipeline in-process: own host (catalog + tenancy) with the MassTransit test harness and the real consumers.
/// <see cref="DrainAsync"/> relays the tenant outbox and waits for consumers until nothing is left.
/// </summary>
public sealed class MessagingDriver : IAsyncDisposable
{
    private const int MaxPasses = 20;

    private readonly IHost _host;

    private MessagingDriver(IHost host)
    {
        _host = host;
        Harness = host.Services.GetRequiredService<ITestHarness>();
    }

    public ITestHarness Harness { get; }

    public IServiceProvider Services => _host.Services;

    public static async Task<MessagingDriver> CreateAsync(IConfiguration source, Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:catalog"] = source.GetConnectionString("catalog"),
            ["ConnectionStrings:postgres"] = source.GetConnectionString("postgres"),
            [AesGcmSecretProtector.MasterKeySetting] = source[AesGcmSecretProtector.MasterKeySetting],
        });
        builder.AddGatehouseCatalog();
        builder.AddGatehouseTenancy();
        builder.Services.AddSingleton<FakeIdentityProvisioner>();
        builder.Services.Replace(ServiceDescriptor.Singleton<Application.Tenancy.IIdentityProvisioner>(sp => sp.GetRequiredService<FakeIdentityProvisioner>()));
        builder.Services.AddAuditModule();
        builder.Services.AddTasksModule();
        builder.Services.AddConnectorsModule();
        builder.Services.AddLiveBuildingInfrastructure();
        builder.Services.AddLiveBuildingModule();
        builder.Services.AddReconciliationWorkerHandlers();
        builder.Services.AddCredentialsWorkerHandlers();
        // ApprovalNotificationConsumer (lab fixes) needs these; without them its retries keep the harness busy and DrainAsync hangs.
        builder.Services.AddOptions<NotificationOptions>();
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddScoped<NotifyApproversHandler>();
        builder.Services.AddMassTransitTestHarness(x =>
        {
            x.AddGatehouseConsumers();
            configure?.Invoke(x);
        });
        var driver = new MessagingDriver(builder.Build());
        await driver.Harness.Start();
        return driver;
    }

    public async Task DrainAsync(TenantId tenantId, CancellationToken ct)
    {
        var relay = Services.GetRequiredService<OutboxRelay>();
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var published = await relay.RunOnceAsync(tenantId, ct);
            if (published == 0)
            {
                return;
            }

            await ConsumedAllAsync(ct);
        }
    }

    // InactivityTask can complete before late consumers start, so wait until every published event of a consumed type was consumed.
    private async Task ConsumedAllAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var published = Harness.Published.Select<FulfillmentFailed>(ct).Count() + Harness.Published.Select<FulfillmentConfirmed>(ct).Count() + Harness.Published.Select<FulfillmentDesired>(ct).Count() + Harness.Published.Select<FulfillmentSuperseded>(ct).Count() + Harness.Published.Select<DoorEventsReceived>(ct).Count() + Harness.Published.Select<RecordExportReceived>(ct).Count();
            var consumed = Harness.Consumed.Select<FulfillmentFailed>(ct).Count() + Harness.Consumed.Select<FulfillmentConfirmed>(ct).Count() + Harness.Consumed.Select<FulfillmentDesired>(ct).Count() + Harness.Consumed.Select<FulfillmentSuperseded>(ct).Count() + Harness.Consumed.Select<DoorEventsReceived>(ct).Count() + Harness.Consumed.Select<RecordExportReceived>(ct).Count();

            // ConnectorCommandAcked has three consumers; wait for each of them.
            var acks = Harness.Published.Select<ConnectorCommandAcked>(ct).Count();
            var acksDone = Harness.GetConsumerHarness<FulfillmentAckConsumer>().Consumed.Select<ConnectorCommandAcked>(ct).Count() >= acks
                && Harness.GetConsumerHarness<ReconciliationExportAckConsumer>().Consumed.Select<ConnectorCommandAcked>(ct).Count() >= acks
                && Harness.GetConsumerHarness<CardCommandAckConsumer>().Consumed.Select<ConnectorCommandAcked>(ct).Count() >= acks;
            if (consumed >= published && acksDone)
            {
                return;
            }

            await Task.Delay(50, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        _host.Dispose();
    }
}
