using Gatehouse.Application.People.Admin.AssignRole;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.People;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.People;

public sealed class AssignRoleValidatorTests
{
    private static readonly AssignRoleValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new AssignRoleRequest(Guid.NewGuid(), AssignedRole.SecurityStaff, Guid.NewGuid())).IsValid);

    [Theory]
    [InlineData(AssignedRole.TenantAdmin, "role.tenant_admin_cannot_be_site_scoped")]
    public void Validate_invalid_request_reports_the_domain_code(AssignedRole role, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new AssignRoleRequest(Guid.NewGuid(), role, Guid.NewGuid()))));
}
