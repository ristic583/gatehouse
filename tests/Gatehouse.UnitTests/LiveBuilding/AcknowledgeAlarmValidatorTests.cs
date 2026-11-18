using Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;
using Gatehouse.UnitTests.Common;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class AcknowledgeAlarmValidatorTests
{
    private static readonly AcknowledgeAlarmValidator Validator = new();

    [Fact]
    public void Validate_valid_request_passes() => Assert.True(Validator.Validate(new AcknowledgeAlarmRequest("Checked on site")).IsValid);

    [Theory]
    [InlineData(null, "alarm.comment_required")]
    [InlineData("  ", "alarm.comment_required")]
    public void Validate_invalid_request_reports_the_domain_code(string? comment, string expected) =>
        Assert.Equal(expected, ValidationAssert.FirstProblemCode(Validator.Validate(new AcknowledgeAlarmRequest(comment))));
}
