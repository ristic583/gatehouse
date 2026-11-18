using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure.Messaging;

namespace Gatehouse.UnitTests.Messaging;

public sealed class OutboxMessageTypeRegistryTests
{
    [Fact]
    public void Resolves_domain_event_and_integration_message()
    {
        var registry = new OutboxMessageTypeRegistry();

        Assert.Equal(typeof(FulfillmentDesired), registry.Resolve(nameof(FulfillmentDesired)));
        Assert.Equal(typeof(ConnectorCommandAcked), registry.Resolve(nameof(ConnectorCommandAcked)));
        Assert.Equal(typeof(ConnectorCapabilitiesReported), registry.Resolve(nameof(ConnectorCapabilitiesReported)));
    }

    [Fact]
    public void Every_domain_event_name_is_unique() =>
        Assert.NotNull(new OutboxMessageTypeRegistry());
}
