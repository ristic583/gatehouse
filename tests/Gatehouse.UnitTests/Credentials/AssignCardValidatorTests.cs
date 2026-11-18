using Gatehouse.Application.Credentials.AssignCard;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Credentials;

public sealed class AssignCardValidatorTests
{
    private static readonly AssignCardValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new AssignCardRequest(" 123456 ")).IsValid);

    [Theory]
    [InlineData(null, "card.invalid_number")]
    [InlineData("12345", "card.invalid_number")]
    [InlineData("12345a", "card.invalid_number")]
    public void Validate_invalid_request_reports_the_domain_code(string? number, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new AssignCardRequest(number!))));
}
