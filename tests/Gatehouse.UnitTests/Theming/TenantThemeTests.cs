using Gatehouse.Domain.Theming;

namespace Gatehouse.UnitTests.Theming;

public sealed class TenantThemeTests
{
    private static TenantTheme Theme(string primary = "#7f1d1d", string primaryForeground = "#ffffff", IReadOnlyDictionary<string, string>? terms = null) =>
        TenantTheme.Create(primary, primaryForeground, "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 8, terms).Value;

    [Fact]
    public void Black_on_white_is_21()
    {
        Assert.Equal(21.0, ContrastChecker.Ratio("#000000", "#ffffff"), 2);
        Assert.Equal(1.0, ContrastChecker.Ratio("#777777", "#777777"), 2);
    }

    [Fact]
    public void Default_theme_passes_AA()
    {
        Assert.Empty(ContrastChecker.Check(TenantTheme.Default));
        Assert.Empty(ContrastChecker.Check(Theme()));
    }

    [Fact]
    public void Low_contrast_pair_is_reported()
    {
        var failing = ContrastChecker.Check(Theme(primary: "#ffff00", primaryForeground: "#ffffff"));

        var pair = Assert.Single(failing);
        Assert.Equal("primary", pair.Name);
        Assert.True(pair.Ratio < ContrastChecker.AaNormalText);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#gggggg")]
    [InlineData(null)]
    public void Invalid_color_is_rejected(string? color)
    {
        var result = TenantTheme.Create(color, "#ffffff", "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 8, null);

        Assert.True(result.IsFailure);
        Assert.Equal("theme.invalid", result.Error.Code);
        Assert.True(TenantTheme.Create("#000000", "#ffffff", "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 25, null).IsFailure);
    }

    [Fact]
    public void Unknown_term_key_is_rejected()
    {
        Assert.True(TenantTheme.Create("#000000", "#ffffff", "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 8, new Dictionary<string, string> { ["Person"] = "Human" }).IsFailure);
        Assert.True(TenantTheme.Create("#000000", "#ffffff", "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 8, new Dictionary<string, string> { ["Area"] = " " }).IsFailure);
        Assert.Equal("Zone", Theme(terms: new Dictionary<string, string> { ["Area"] = " Zone " }).Terms["Area"]);
    }
}
