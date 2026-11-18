using Gatehouse.Application.Credentials.DeactivateCard;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Credentials;

public sealed class DeactivateCardValidatorTests
{
    private static readonly DeactivateCardValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new DeactivateCardRequest("Lost")).IsValid);

    [Theory]
    [InlineData(null, "card.reason_required")]
    [InlineData(" ", "card.reason_required")]
    public void Validate_invalid_request_reports_the_domain_code(string? reason, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new DeactivateCardRequest(reason!))));
}
