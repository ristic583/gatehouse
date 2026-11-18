using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.FulfillmentActions;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Fulfillment;

public sealed class FailFulfillmentValidatorTests
{
    private static readonly FailFulfillmentValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new FailFulfillmentDto("Controller offline")).IsValid);

    [Theory]
    [InlineData(null, "fulfillment.reason_required")]
    [InlineData("   ", "fulfillment.reason_required")]
    public void Validate_invalid_request_reports_the_domain_code(string? reason, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new FailFulfillmentDto(reason))));
}
