namespace Gatehouse.Application.Theming;

/// <summary>The tenant theme (spec §11); also the body of <c>PUT /api/v1/admin/theme</c>. Map state colors are not part of it.</summary>
public sealed record ThemeDto(
    string Primary,
    string PrimaryForeground,
    string Accent,
    string AccentForeground,
    string Background,
    string Foreground,
    int RadiusPx,
    IReadOnlyDictionary<string, string> Terms);
