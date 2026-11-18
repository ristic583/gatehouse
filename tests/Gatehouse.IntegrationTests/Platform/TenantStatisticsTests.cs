using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary>ADR-009 §2: statistics are counts only, written via the tenant scope factory.</summary>
public sealed class TenantStatisticsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Statistics_count_without_personal_data()
    {
        var ct = TestContext.Current.CancellationToken;
        // The table holds counts and timestamps only: no string column besides the key.
        Assert.All(typeof(TenantStatistics).GetProperties(), p => Assert.True(
            p.Name == nameof(TenantStatistics.TenantId) || p.PropertyType == typeof(int) || p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?),
            p.Name));

        var processed = await _api.Services.GetRequiredService<TenantStatisticsCollector>().RunOnceAsync(ct);

        Assert.Equal(2, processed); // nordlys and bricks; the suspended tenant is skipped
        int people, sites, areas;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            people = await db.Persons.CountAsync(p => p.AnonymizedAt == null, ct);
            sites = await db.Sites.CountAsync(ct);
            areas = await db.Areas.CountAsync(ct);
        }

        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var dto = (await client.GetFromJsonAsync<PlatformTenantDto>(new Uri("/api/v1/platform/tenants/nordlys", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.NotNull(dto.Statistics);
        Assert.Equal(people, dto.Statistics.People);
        Assert.Equal(sites, dto.Statistics.Sites);
        Assert.Equal(areas, dto.Statistics.Areas);
        Assert.True(dto.Statistics.TenantAdmins >= 1);
        Assert.Equal(people, dto.People);
        var json = await client.GetStringAsync(new Uri("/api/v1/platform/tenants/nordlys", UriKind.Relative), ct);
        Assert.DoesNotContain("@nordlys.test", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DemoUser.Anna.Username, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task First_admin_sign_in_marks_activated()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(client, request, ct);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        await _api.Services.GetRequiredService<ITenantProvisioningRunner>().RunOnceAsync(ct);
        var collector = _api.Services.GetRequiredService<TenantStatisticsCollector>();
        var id = new TenantId(request.TenantId);

        await collector.CollectAsync(id, ct);
        Assert.Null((await ProvisioningAsync(id, ct)).AdminActivatedAt);

        // First sign-in links the person to the identity subject.
        using var admin = _api.CreateClientFor($"kc-{Guid.NewGuid():N}", request.AdminEmail, request.TenantId);
        using var me = await admin.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await collector.CollectAsync(id, ct);

        Assert.NotNull((await ProvisioningAsync(id, ct)).AdminActivatedAt);
        using var invite = await client.PostAsync(new Uri($"/api/v1/platform/tenants/{request.TenantId}/admin-invite", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.Conflict, invite.StatusCode);
        Assert.Contains("tenant.admin_already_active", await invite.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    private async Task<TenantProvisioning> ProvisioningAsync(TenantId id, CancellationToken ct)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantProvisionings.AsNoTracking().SingleAsync(p => p.TenantId == id, ct);
    }
}
