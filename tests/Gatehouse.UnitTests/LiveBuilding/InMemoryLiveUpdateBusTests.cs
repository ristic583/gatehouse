using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Infrastructure.LiveBuilding;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class InMemoryLiveUpdateBusTests
{
    [Fact]
    public async Task Publish_reaches_subscriber_with_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = new InMemoryLiveUpdateBus();
        var received = new List<(string Tenant, LiveUpdateDto Update)>();
        await bus.SubscribeAsync((tenant, update) =>
        {
            received.Add((tenant, update));
            return Task.CompletedTask;
        }, ct);
        var publisher = new TenantLiveUpdatePublisher(bus, new FixedTenantContext("nordlys"));
        var update = new LiveUpdateDto(LiveUpdateKind.DoorState, Guid.NewGuid());

        await publisher.PublishAsync(update, ct);

        var (tenant, got) = Assert.Single(received);
        Assert.Equal("nordlys", tenant);
        Assert.Same(update, got);
    }
}
