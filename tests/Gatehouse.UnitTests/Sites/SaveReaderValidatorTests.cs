using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Sites;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.Sites;

public sealed class SaveReaderValidatorTests
{
    private static readonly SaveReaderValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new SaveReaderRequest("In", ReaderDirection.In, new PointDto(1, 1), null)).IsValid);

    [Theory]
    [InlineData("", "reader.invalid_name")]
    public void Validate_invalid_request_reports_the_domain_code(string name, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new SaveReaderRequest(name, ReaderDirection.In, new PointDto(1, 1), null))));
}
