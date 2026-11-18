using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Theming;

/// <summary>
/// Per-tenant design tokens (spec §11): colors as <c>#rrggbb</c>, radius 0-24 px, terminology overrides for a fixed set of nouns.
/// Map state colors, icons and labels are not part of the theme (ADR-007).
/// </summary>
public sealed partial record TenantTheme(
    string Primary,
    string PrimaryForeground,
    string Accent,
    string AccentForeground,
    string Background,
    string Foreground,
    int RadiusPx,
    IReadOnlyDictionary<string, string> Terms)
{
    public const int MaxRadiusPx = 24;
    public const int MaxTermLength = 30;

    /// <summary>The nouns a tenant may rename (e.g. "Area" → "Zone").</summary>
    public static readonly IReadOnlySet<string> TermKeys = new HashSet<string>(StringComparer.Ordinal) { "Area", "Site", "Floor", "Door" };

    /// <summary>Matches the light values in <c>src/web/app/globals.css</c>.</summary>
    public static readonly TenantTheme Default = new(
        "#171717", "#fafafa", "#f5f5f5", "#171717", "#ffffff", "#0a0a0a", 10, new Dictionary<string, string>());

    public static Result<TenantTheme> Create(
        string? primary,
        string? primaryForeground,
        string? accent,
        string? accentForeground,
        string? background,
        string? foreground,
        int radiusPx,
        IReadOnlyDictionary<string, string>? terms)
    {
        string?[] colors = [primary, primaryForeground, accent, accentForeground, background, foreground];
        if (colors.Any(c => c is null || !ColorPattern().IsMatch(c)))
        {
            return Invalid("Colors must be #rrggbb.");
        }

        if (radiusPx is < 0 or > MaxRadiusPx)
        {
            return Invalid($"Radius must be 0-{MaxRadiusPx} px.");
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in terms ?? new Dictionary<string, string>())
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (!TermKeys.Contains(key) || trimmed.Length is 0 or > MaxTermLength || trimmed.Any(char.IsControl))
            {
                return Invalid(
                    $"Terms may rename only {string.Join(", ", TermKeys.Order(StringComparer.Ordinal))} to "
                        + $"1-{MaxTermLength} printable characters.");
            }

            normalized[key] = trimmed;
        }

        return new TenantTheme(
            primary!.ToLowerInvariant(),
            primaryForeground!.ToLowerInvariant(),
            accent!.ToLowerInvariant(),
            accentForeground!.ToLowerInvariant(),
            background!.ToLowerInvariant(),
            foreground!.ToLowerInvariant(),
            radiusPx,
            normalized);
    }

    private static Error Invalid(string message) => Error.Validation("theme.invalid", message);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();
}
