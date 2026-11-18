using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Sites;

public sealed class SaveDoorValidatorTests
{
    private static readonly SaveDoorValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SaveDoorRequest("Main", new PointDto(1, 1), null)).IsValid);

    [Theory]
    [InlineData("", "door.invalid_name")]
    [InlineData("  ", "door.invalid_name")]
    public void Validate_invalid_request_reports_the_domain_code(string name, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SaveDoorRequest(name, new PointDto(1, 1), null))));
}
