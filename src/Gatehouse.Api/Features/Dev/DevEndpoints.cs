using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Seeding;

namespace Gatehouse.Api.Features.Dev;

public static class DevEndpoints
{
    public const string EnableResetSetting = "Gatehouse:Dev:EnableReset";

    /// <summary>SEC-02: mapped only in Development <b>and</b> with <see cref="EnableResetSetting"/>; a copied setting cannot expose
    /// it.</summary>
    public static IEndpointRouteBuilder MapDevEndpoints(
        this IEndpointRouteBuilder endpoints, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>(EnableResetSetting))
        {
            return endpoints;
        }

        endpoints.MapPost("/api/v1/dev/reset-demo-access", ResetDemoAccessAsync)
            .RequireAuthorization(Policies.TenantAdmin)
            .WithName("ResetDemoAccess");

        return endpoints;
    }

    private static async Task<IResult> ResetDemoAccessAsync(
        ITenantContext tenantContext, DemoSeeder seeder, string? fulfillmentMode, CancellationToken ct)
    {
        FulfillmentMode? mode = null;
        if (fulfillmentMode is not null)
        {
            if (!Enum.TryParse<FulfillmentMode>(fulfillmentMode, false, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Error.Validation("validation.failed", "fulfillmentMode must be Manual or Connector.").ToProblem();
            }

            mode = parsed;
        }

        await seeder.ResetAccessDataAsync(tenantContext.TenantId, mode, ct);
        return Results.NoContent();
    }
}
