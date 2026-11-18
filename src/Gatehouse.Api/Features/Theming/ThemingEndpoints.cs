using System.Globalization;
using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Theming;
using Gatehouse.Application.Theming.GetTheme;
using Gatehouse.Application.Theming.SaveTheme;
using Gatehouse.Domain.Theming;

namespace Gatehouse.Api.Features.Theming;

public static class ThemingEndpoints
{
    public static IEndpointRouteBuilder MapThemingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/theme", async (GetThemeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetThemeQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.TenantMember)
            .WithName("GetTheme");

        endpoints.MapPut("/api/v1/admin/theme", async (ThemeDto request, SaveThemeHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(new SaveThemeCommand(request), ct);
                if (result.FailingPairs.Count > 0)
                {
                    return result.FailingPairs
                        .ToDictionary(p => p.Name, p => new[] { ContrastMessage(p.Ratio) })
                        .ToValidationProblem("theme.contrast_failed", "Some color pairs do not meet WCAG AA contrast.");
                }

                return result.Error is { } error ? error.ToProblem() : Results.Ok(result.Theme);
            })
            .RequireAuthorization(Policies.TenantAdmin);
        return endpoints;
    }

    private static string ContrastMessage(double ratio) =>
        string.Create(CultureInfo.InvariantCulture, $"{ratio:0.00}:1 is below {ContrastChecker.AaNormalText}:1");
}
