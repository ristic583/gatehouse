using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Sites;

public sealed class SaveSiteValidatorTests
{
    private static readonly SaveSiteValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SaveSiteRequest(" HQ ", "HQ-1", "Europe/Berlin")).IsValid);

    [Theory]
    [InlineData(" ", "HQ", "Europe/Berlin", "site.invalid_name")]
    [InlineData(null, "HQ", "Europe/Berlin", "site.invalid_name")]
    [InlineData("HQ", "hq", "Europe/Berlin", "site.invalid_code")]
    [InlineData("HQ", "HQ", "Mars/Base", "site.invalid_time_zone")]
    public void Validate_invalid_request_reports_the_domain_code(string? name, string? code, string? zone, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SaveSiteRequest(name!, code!, zone!))));
}
