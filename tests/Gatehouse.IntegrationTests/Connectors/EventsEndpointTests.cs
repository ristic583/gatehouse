using System.Net;
using System.Net.Http.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class EventsEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string CardNumber = "100200300";
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
        await ClearOutboxAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static DoorEvent NewEvent(Guid? id = null) => new(
        id ?? Guid.CreateVersion7(), DateTimeOffset.UtcNow, DoorEventType.AccessGranted, "D-CPH-SERVER", "R-CPH-SERVER",
        CardNumber, Guid.NewGuid().ToString(), ReaderDirection.In);

    [Fact]
    public async Task Accepts_batch_and_enqueues_door_events_received()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest([NewEvent(), NewEvent()]), cert);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DoorEventsResponse>(ConnectorJson.Options, ct);
        Assert.Equal(new DoorEventsResponse(2, 0), body);
        var payloads = await OutboxPayloadsAsync(ct);
        var payload = Assert.Single(payloads);
        Assert.Contains("D-CPH-SERVER", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_event_id_is_counted_not_republished()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var first = NewEvent();
        await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest([first]), cert);
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest([first, NewEvent()]), cert);

        Assert.Equal(new DoorEventsResponse(1, 1), await response.Content.ReadFromJsonAsync<DoorEventsResponse>(ConnectorJson.Options, ct));
        Assert.Equal(2, (await OutboxPayloadsAsync(ct)).Count);
    }

    [Fact]
    public async Task Card_number_is_hashed_in_outbox_payload()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest([NewEvent()]), cert);

        var payload = Assert.Single(await OutboxPayloadsAsync(ct));
        Assert.DoesNotContain(CardNumber, payload, StringComparison.Ordinal);
        Assert.Contains(_factory.Services.GetRequiredService<Application.Connectors.ICardNumberHasher>().Hash(new Domain.Common.TenantId("nordlys"), CardNumber)!, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_batch_is_400_and_oversized_is_413()
    {
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var empty = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest([]), cert);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var many = Enumerable.Range(0, 501).Select(_ => NewEvent()).ToList();
        var oversized = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/events", new DoorEventsRequest(many), cert);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    private async Task<List<string>> OutboxPayloadsAsync(CancellationToken ct)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "DoorEventsReceived").Select(m => m.Payload).ToListAsync(ct);
    }

    private async Task ClearOutboxAsync(CancellationToken ct)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.ExecuteDeleteAsync(ct);
    }
}
