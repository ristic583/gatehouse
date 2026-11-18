using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Fulfillment;

public sealed class SetAccessLevelMappingsValidatorTests
{
    private static readonly SetAccessLevelMappingsValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() =>
        Assert.True(Validator.Validate([new(Guid.NewGuid(), "L1", null), new(Guid.NewGuid(), "L2", "Lab")]).IsValid);

    [Fact]
    public void Validate_area_mapped_twice_reports_mapping_invalid()
    {
        var area = Guid.NewGuid();
        List<AccessLevelMappingRequest> request = [new(area, "L1", null), new(area, "L2", null)];
        Assert.Equal("mapping.invalid", ValidationAssert.FirstProblemCode(Validator.Validate(request)));
    }

    [Fact]
    public void Validate_blank_vendor_reference_reports_mapping_invalid() =>
        Assert.Equal("mapping.invalid", ValidationAssert.FirstProblemCode(Validator.Validate([new(Guid.NewGuid(), " ", "Lab")])));
}
