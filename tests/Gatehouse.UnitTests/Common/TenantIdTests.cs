using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class TenantIdTests
{
    [Theory]
    [InlineData("nordlys")]
    [InlineData("bricks")]
    [InlineData("acme-2")]
    public void Valid_slug_is_accepted(string slug)
    {
        var result = TenantId.Create(slug);

        Assert.True(result.IsSuccess);
        Assert.Equal(slug, result.Value.Value);
        Assert.Equal(slug, result.Value.ToString());
    }

    [Theory]
    [InlineData("Nordlys")]
    [InlineData("ab")]
    [InlineData("1abc")]
    [InlineData(" nordlys")]
    [InlineData("")]
    public void Uppercase_or_short_slug_is_rejected_with_code(string slug)
    {
        var result = TenantId.Create(slug);

        Assert.True(result.IsFailure);
        Assert.Equal("tenant.invalid_id", result.Error.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Database_name_replaces_dashes()
    {
        var id = TenantId.Create("acme-north-1").Value;

        Assert.Equal("gh_tenant_acme_north_1", id.DatabaseName);
    }
}
