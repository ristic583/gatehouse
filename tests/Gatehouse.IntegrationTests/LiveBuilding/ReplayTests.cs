using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.IntegrationTests.LiveBuilding;

/// <summary>24 h replay events (M9-10); door events and the extra staff role are removed afterwards.</summary>
public sealed class ReplayTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly DateTimeOffset _t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
    private RoleAssignmentId? _role;

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Returns_events_in_window_oldest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var second = Event(60, Wire.DoorEventType.AccessDenied);
        var first = Event(0, Wire.DoorEventType.AccessGranted);
        await IngestAsync(second, first);
        var floorId = await FloorOfAsync("CPH-LAB");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        var events = await jonas.GetFromJsonAsync<List<LiveDoorEventDto>>(Url(floorId, _t0.AddMinutes(-1), DateTimeOffset.UtcNow), ApiJson.Options, ct);

        var mine = events!.Where(e => e.EventId == first.EventId || e.EventId == second.EventId).ToList();
        Assert.Equal([first.EventId, second.EventId], mine.Select(e => e.EventId));
        Assert.Equal("Lab", mine[0].AreaName);
    }

    [Fact]
    public async Task Window_over_24h_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorOfAsync("CPH-LAB");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var tooLong = await jonas.GetAsync(Url(floorId, DateTimeOffset.UtcNow.AddHours(-30), DateTimeOffset.UtcNow), ct);
        using var tooOld = await jonas.GetAsync(Url(floorId, DateTimeOffset.UtcNow.AddHours(-48), DateTimeOffset.UtcNow.AddHours(-40)), ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("live.invalid_range", await tooLong.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, tooOld.StatusCode);
    }

    [Fact]
    public async Task Other_site_staff_gets_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorOfAsync("CPH-LAB");
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = await db.Persons.SingleAsync(p => p.Email == "person-02@nordlys.test", ct);
            var site = await db.Sites.SingleAsync(s => s.Code == "AAR-WH", ct);
            var role = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, site.Id).Value;
            db.RoleAssignments.Add(role);
            await db.SaveChangesAsync(ct);
            _role = role.Id;
        }

        using var scoped = _api.CreateClientFor("test-person-02", "person-02@nordlys.test", Tenant);
        using var response = await scoped.GetAsync(Url(floorId, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Uri Url(Guid floorId, DateTimeOffset from, DateTimeOffset to) =>
        new($"/api/v1/live/floors/{floorId}/events?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", UriKind.Relative);

    private NormalizedDoorEvent Event(int seconds, Wire.DoorEventType type) =>
        new(Guid.CreateVersion7(), _t0.AddSeconds(seconds), type, "D-CPH-LAB", null, null, null, Wire.ReaderDirection.In);

    private async Task<Guid> FloorOfAsync(string areaCode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Areas.AsNoTracking().SingleAsync(a => a.Code == areaCode, TestContext.Current.CancellationToken)).FloorId.Value;
    }

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
}
