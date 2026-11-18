using Gatehouse.Application.Credentials.SetPin;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Credentials;

public sealed class SetPinValidatorTests
{
    private static readonly SetPinValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SetPinRequest("1234")).IsValid);

    [Theory]
    [InlineData(null, "pin.invalid")]
    [InlineData("123", "pin.invalid")]
    [InlineData("123456789", "pin.invalid")]
    public void Validate_invalid_request_reports_the_domain_code(string? pin, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SetPinRequest(pin))));
}
