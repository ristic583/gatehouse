using System.Globalization;

namespace Gatehouse.Domain.Theming;

/// <summary>WCAG 2.x contrast (relative luminance); AA for normal text is 4.5:1.</summary>
public static class ContrastChecker
{
    public const double AaNormalText = 4.5;

    public static double Ratio(string hexA, string hexB)
    {
        var a = Luminance(hexA);
        var b = Luminance(hexB);
        var (lighter, darker) = a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>The three required pairs with their ratios.</summary>
    public static IReadOnlyList<ContrastPair> Pairs(TenantTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return
        [
            new("primary", theme.PrimaryForeground, theme.Primary, Ratio(theme.PrimaryForeground, theme.Primary)),
            new("accent", theme.AccentForeground, theme.Accent, Ratio(theme.AccentForeground, theme.Accent)),
            new("background", theme.Foreground, theme.Background, Ratio(theme.Foreground, theme.Background)),
        ];
    }

    /// <summary>Pairs below AA (empty when the theme passes).</summary>
    public static IReadOnlyList<ContrastPair> Check(TenantTheme theme) => Pairs(theme).Where(p => p.Ratio < AaNormalText).ToList();

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(1 + offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }
}
