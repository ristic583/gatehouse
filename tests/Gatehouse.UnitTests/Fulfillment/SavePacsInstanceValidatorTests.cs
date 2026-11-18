using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Fulfillment;

public sealed class SavePacsInstanceValidatorTests
{
    private static readonly SavePacsInstanceValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SavePacsInstanceRequest("Main", PacsVendor.LegacyPacs, [Guid.NewGuid()])).IsValid);

    [Theory]
    [InlineData(null, "pacs.invalid_name")]
    [InlineData(" ", "pacs.invalid_name")]
    public void Validate_invalid_request_reports_the_domain_code(string? name, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SavePacsInstanceRequest(name!, PacsVendor.LegacyPacs, [Guid.NewGuid()]))));
}
