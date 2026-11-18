using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class CardNumberMaskTests
{
    [Theory]
    [InlineData("1234567890", "****7890")]
    [InlineData("1234", "****1234")]
    [InlineData("123", "****")]
    [InlineData(null, "****")]
    public void Masks_all_but_last_four(string? input, string expected)
    {
        Assert.Equal(expected, CardNumberMask.Mask(input));
    }
}
