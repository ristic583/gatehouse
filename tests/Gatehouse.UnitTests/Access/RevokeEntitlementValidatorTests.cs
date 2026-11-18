using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.Revocation;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Access;

public sealed class RevokeEntitlementValidatorTests
{
    private static readonly RevokeEntitlementValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new RevokeEntitlementDto("Left the project")).IsValid);

    [Theory]
    [InlineData(null, "entitlement.revocation_reason_required")]
    [InlineData(" ", "entitlement.revocation_reason_required")]
    public void Validate_invalid_request_reports_the_domain_code(string? reason, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new RevokeEntitlementDto(reason!))));
}
