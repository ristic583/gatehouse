using Gatehouse.Domain.Identity;
using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.Identity;

public sealed class MagicLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static MagicLink Link() => MagicLink.Issue(PersonId.New(), new string('a', 64), PersonId.New(), Now, TimeSpan.FromMinutes(30));

    [Fact]
    public void Magic_link_redeems_once()
    {
        var link = Link();

        Assert.True(link.Redeem(Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(Now.AddMinutes(1), link.UsedAt);
        Assert.Equal("magic_link.invalid", link.Redeem(Now.AddMinutes(2)).Error.Code);
    }

    [Fact]
    public void Expired_magic_link_is_invalid()
    {
        var link = Link();

        Assert.Equal(Now.AddMinutes(30), link.ExpiresAt);
        Assert.Equal("magic_link.invalid", link.Redeem(Now.AddMinutes(30)).Error.Code);
        Assert.Null(link.UsedAt);
    }
}
