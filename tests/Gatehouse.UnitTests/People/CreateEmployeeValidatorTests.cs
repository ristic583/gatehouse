using Gatehouse.Application.People.Admin.CreateEmployee;
using Gatehouse.Application.People.Dtos;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.People;

public sealed class CreateEmployeeValidatorTests
{
    private static readonly CreateEmployeeValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new CreateEmployeeRequest("ana@example.com", "Ana", "Ng", null, null)).IsValid);

    [Theory]
    [InlineData("ana@example.com", "", "Ng", "person.invalid_name")]
    [InlineData("ana@example.com", "Ana", " ", "person.invalid_name")]
    [InlineData("ana.example.com", "Ana", "Ng", "person.invalid_email")]
    public void Validate_invalid_request_reports_the_domain_code(string email, string given, string family, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new CreateEmployeeRequest(email, given, family, null, null))));
}
