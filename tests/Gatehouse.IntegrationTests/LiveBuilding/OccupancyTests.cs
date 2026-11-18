using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.IngestDoorEvents;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.IntegrationTests.LiveBuilding;

/// <summary>Occupancy counters from In/Out readers (M9-09) on CPH-LAB; door events and alarms are cleared afterwards.</summary>
public sealed class OccupancyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly DateTimeOffset _t0 = DateTimeOffset.UtcNow.AddMinutes(-5);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task In_and_out_swipes_update_count()
    {
        await IngestAsync(
            Event(0, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In),
            Event(1, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In),
            Event(2, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.Out));

        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Count_never_below_zero()
    {
        await IngestAsync(
            Event(0, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.Out),
            Event(1, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.Out),
            Event(2, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In));

        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Same_card_in_twice_counts_one_person()
    {
        await IngestAsync(
            Event(0, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In) with { CardNumberHash = "card-hash-1" },
            Event(1, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In) with { CardNumberHash = "card-hash-1" },
            Event(2, Wire.DoorEventType.AccessGranted, Wire.ReaderDirection.In) with { CardNumberHash = "card-hash-2" });

        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Denied_swipe_does_not_count()
    {
        await IngestAsync(
            Event(0, Wire.DoorEventType.AccessDenied, Wire.ReaderDirection.In),
            Event(1, Wire.DoorEventType.AccessDenied, Wire.ReaderDirection.In));

        Assert.Equal(0, await CountAsync());
    }

    private NormalizedDoorEvent Event(int seconds, Wire.DoorEventType type, Wire.ReaderDirection direction) =>
        new(Guid.CreateVersion7(), _t0.AddSeconds(seconds), type, "D-CPH-LAB", null, null, null, direction);

    private async Task IngestAsync(params NormalizedDoorEvent[] events)
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new DoorEventsReceived(Tenant, instance.Id, new ConnectorId(Guid.CreateVersion7()), DateTimeOffset.UtcNow, events),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);
    }

    private async Task<long> CountAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        AreaId area;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            area = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-LAB", ct)).Id;
        }

        var messaging = await _api.MessagingAsync();
        await using var tenantScope = await messaging.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantId(Tenant), ct);
        var counts = await tenantScope.ServiceProvider.GetRequiredService<IOccupancyStore>()
            .GetAsync([area], IngestDoorEventsHandler.LocalDate(_t0, "Europe/Copenhagen"), ct);
        return counts.GetValueOrDefault(area);
    }
}
