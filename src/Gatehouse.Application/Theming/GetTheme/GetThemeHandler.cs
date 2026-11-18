using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Theming.GetTheme;

/// <summary><c>GET /api/v1/theme</c>: the tenant theme, or the default when none or an invalid one is stored.</summary>
public sealed class GetThemeHandler(ThemeStore store)
{
    public async Task<Result<ThemeDto>> HandleAsync(GetThemeQuery query, CancellationToken ct) =>
        ThemeStore.ToDto(await store.GetAsync(ct));
}
