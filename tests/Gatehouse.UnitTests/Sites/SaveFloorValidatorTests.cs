using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Sites;

public sealed class SaveFloorValidatorTests
{
    private static readonly SaveFloorValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SaveFloorRequest("Ground", 0, 1000, 800)).IsValid);

    [Theory]
    [InlineData("", 0, 1000, 800, "floor.invalid_name")]
    [InlineData("G", -6, 1000, 800, "floor.invalid_level")]
    [InlineData("G", 0, 99, 800, "floor.invalid_plan_size")]
    [InlineData("G", 0, 1000, 10001, "floor.invalid_plan_size")]
    public void Validate_invalid_request_reports_the_domain_code(string name, int level, int width, int height, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SaveFloorRequest(name, level, width, height))));
}
