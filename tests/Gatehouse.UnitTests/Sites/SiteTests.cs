using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Sites;

public sealed class SiteTests
{
    [Fact]
    public void Update_validates_code()
    {
        var site = Site.Create("HQ", "CPH-HQ", "Europe/Copenhagen").Value;

        var invalid = site.Update("HQ", "cph hq", "Europe/Copenhagen");
        var valid = site.Update("Head office", "CPH-HQ2", "Europe/Berlin");

        Assert.Equal("site.invalid_code", invalid.Error.Code);
        Assert.True(valid.IsSuccess);
        Assert.Equal(("Head office", "CPH-HQ2", "Europe/Berlin"), (site.Name, site.Code, site.TimeZoneId));
    }

    [Fact]
    public void Invalid_time_zone_is_rejected()
    {
        var result = Site.Create("HQ", "CPH-HQ", "Mars/Olympus");
        Assert.Equal("site.invalid_time_zone", result.Error.Code);
    }

    [Fact]
    public void Valid_site_is_created()
    {
        Assert.True(Site.Create("HQ", "CPH-HQ", "Europe/Copenhagen").IsSuccess);
    }
}
