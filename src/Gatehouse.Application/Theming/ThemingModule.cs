using Gatehouse.Application.Theming.GetTheme;
using Gatehouse.Application.Theming.SaveTheme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Theming;

public static class ThemingModule
{
    public static IServiceCollection AddThemingModule(this IServiceCollection services)
    {
        services.TryAddScoped<ThemeStore>();
        services.TryAddScoped<GetThemeHandler>();
        services.TryAddScoped<SaveThemeHandler>();
        return services;
    }
}
