using System.Text.RegularExpressions;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Shape rules shared by the site admin validators; the handlers trim names and codes before Domain sees them.</summary>
public static partial class SiteRules
{
    public static bool IsName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;

    [GeneratedRegex("^[A-Z0-9-]{2,20}$", RegexOptions.CultureInvariant)]
    public static partial Regex SiteCode();

    [GeneratedRegex("^[A-Z0-9-]{2,30}$", RegexOptions.CultureInvariant)]
    public static partial Regex AreaCode();
}
