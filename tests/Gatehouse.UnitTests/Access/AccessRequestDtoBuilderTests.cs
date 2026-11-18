using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.UnitTests.Access;

public sealed class AccessRequestDtoBuilderTests
{
    [Fact]
    public void Revoke_status_is_summarized_separately()
    {
        var items = new List<(FulfillmentKind, FulfillmentStatus)>
        {
            (FulfillmentKind.Grant, FulfillmentStatus.Confirmed),
            (FulfillmentKind.Revoke, FulfillmentStatus.Pending),
        };

        Assert.Equal("Confirmed", AccessRequestDtoBuilder.SummarizeFulfillment(items, FulfillmentKind.Grant));
        Assert.Equal("Pending", AccessRequestDtoBuilder.SummarizeFulfillment(items, FulfillmentKind.Revoke));
        Assert.Null(AccessRequestDtoBuilder.SummarizeFulfillment([(FulfillmentKind.Grant, FulfillmentStatus.Confirmed)], FulfillmentKind.Revoke));
    }

    [Fact]
    public void Superseded_grants_are_ignored()
    {
        var items = new List<(FulfillmentKind, FulfillmentStatus)>
        {
            (FulfillmentKind.Grant, FulfillmentStatus.Superseded),
            (FulfillmentKind.Grant, FulfillmentStatus.Confirmed),
        };

        Assert.Equal("Confirmed", AccessRequestDtoBuilder.SummarizeFulfillment(items, FulfillmentKind.Grant));
        Assert.Null(AccessRequestDtoBuilder.SummarizeFulfillment([(FulfillmentKind.Grant, FulfillmentStatus.Superseded)], FulfillmentKind.Grant));
    }
}
