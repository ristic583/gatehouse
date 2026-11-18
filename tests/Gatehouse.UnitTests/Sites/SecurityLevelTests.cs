using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Sites;

public sealed class SecurityLevelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Max_duration_out_of_range_is_rejected(int days)
    {
        var result = SecurityLevel.Create("Low", 1, ApproverChain.Manager, days);
        Assert.Equal("security_level.invalid_max_duration", result.Error.Code);
    }
}
