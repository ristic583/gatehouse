using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gatehouse.Application.Common.Diagnostics;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Connector.Contracts;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Telemetry;

/// <summary>Trace context and metrics (M9-08); rows written here are removed afterwards.</summary>
public sealed class TraceContextTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly ActivitySource TestSource = new("Gatehouse.Tests");

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly Domain.People.PersonId _personId = Domain.People.PersonId.New();

    private string Partition => $"person:{_personId.Value}";
    private ActivityListener? _listener;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name is "Gatehouse" or "Gatehouse.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public async ValueTask DisposeAsync()
    {
        _listener?.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Command_carries_traceparent_from_request()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = TestSource.StartActivity("request");
        Assert.NotNull(request);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.AsNoTracking().FirstAsync(ct);
        var factory = scope.ServiceProvider.GetRequiredService<ConnectorCommandFactory>();

        var command = await factory.CreateForPersonAsync(pacs.Id, _personId, ConnectorCommandTypes.SetPin,
            new Connector.Contracts.Commands.SetPin("p", "1", "vault:AA:local-v1"), $"SetPin:{Partition}:trace", CommandPriority.Normal, ct);
        db.ConnectorCommands.Add(command);
        await db.SaveChangesAsync(ct);

        var stored = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == command.Id, ct);
        Assert.Equal(request.Id, stored.TraceParent);
    }

    [Fact]
    public async Task Outbox_message_restores_parent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = TestSource.StartActivity("request");
        Assert.NotNull(request);
        string? traceParent;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var row = new OutboxMessage { Id = Guid.CreateVersion7(), OccurredAt = DateTimeOffset.UtcNow, Type = "TraceProbe", TraceParent = GatehouseTelemetry.CurrentTraceParent() };
            db.Set<OutboxMessage>().Add(row);
            await db.SaveChangesAsync(ct);
            traceParent = (await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == row.Id, ct)).TraceParent;
        }

        request.Stop();
        using var publish = OutboxRelay.StartPublishActivity("TraceProbe", traceParent);

        Assert.NotNull(publish);
        Assert.Equal(request.TraceId, publish.TraceId);
        Assert.Equal(request.SpanId, publish.ParentSpanId);
    }

    [Fact]
    public async Task Metrics_are_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var recorded = new List<string>();
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == GatehouseTelemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>((instrument, _, _, _) => recorded.Add(instrument.Name));
        meters.SetMeasurementEventCallback<double>((instrument, _, _, _) => recorded.Add(instrument.Name));
        meters.Start();

        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.AsNoTracking().FirstAsync(p => p.Name == "HQ-Copenhagen", ct);
        var connector = new ConnectorContext();
        connector.Set(new TenantId("nordlys"), pacs.Id, ConnectorId.New());
        var poll = ActivatorUtilities.CreateInstance<ConnectorCommandDelivery>(scope.ServiceProvider);
        await poll.SelectAndDeliverAsync(connector, 0, DateTimeOffset.UtcNow, ct);

        Assert.Contains("gatehouse.connector.queue_depth", recorded);
    }
}
