using System.Net.Http.Json;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary>Arranges platform operators and tenant requests for platform tests.</summary>
public static class PlatformTestData
{
    public static readonly Uri Tenants = new("/api/v1/platform/tenants", UriKind.Relative);

    /// <summary>Creates an active operator and returns a client authenticated as them.</summary>
    public static async Task<HttpClient> OperatorClientAsync(GatehouseApiFactory api, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        var email = $"op-{Guid.NewGuid():N}@platform.test";
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.PlatformOperators.Add(PlatformOperator.Create(email, "Test operator", DateTimeOffset.UtcNow).Value);
            await catalog.SaveChangesAsync(ct);
        }

        return api.CreatePlatformClient($"kc-{Guid.NewGuid():N}", email);
    }

    public static CreateTenantRequest NewTenant(string? domain = null)
    {
        var id = "t" + Guid.NewGuid().ToString("N")[..10];
        domain ??= $"{id}.example";
        return new CreateTenantRequest(id, $"Tenant {id}", domain, $"admin@{domain}", "Ada", "Admin", "Europe/Copenhagen");
    }

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, CreateTenantRequest request, CancellationToken ct) =>
        client.PostAsJsonAsync(Tenants, request, ApiJson.Options, ct);

    public static async Task<List<string>> AuditActionsAsync(GatehouseApiFactory api, string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        await using var scope = api.Services.CreateAsyncScope();
        var id = new Domain.Common.TenantId(tenantId);
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PlatformAuditEntries.AsNoTracking()
            .Where(e => e.TenantId == id).OrderBy(e => e.Sequence).Select(e => e.Action).ToListAsync(ct);
    }
}
