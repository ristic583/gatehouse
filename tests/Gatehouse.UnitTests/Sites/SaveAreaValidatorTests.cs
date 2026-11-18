using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Sites;

public sealed class SaveAreaValidatorTests
{
    private static readonly SaveAreaValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SaveAreaRequest("Lab", "LAB-1", [], Guid.NewGuid(), null)).IsValid);

    [Theory]
    [InlineData(" ", "LAB", "area.invalid_name")]
    [InlineData("Lab", "L", "area.invalid_code")]
    [InlineData("Lab", "lab-1", "area.invalid_code")]
    public void Validate_invalid_request_reports_the_domain_code(string name, string code, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SaveAreaRequest(name, code, [], Guid.NewGuid(), null))));
}
