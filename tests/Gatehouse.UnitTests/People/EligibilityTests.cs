using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.People;

public sealed class EligibilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void End_before_start_is_rejected()
    {
        var result = Eligibility.Create(Start, Start.AddDays(-1), "Europe/Copenhagen");
        Assert.Equal("person.invalid_eligibility", result.Error.Code);
    }
}
