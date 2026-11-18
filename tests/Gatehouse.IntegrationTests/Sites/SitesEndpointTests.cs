using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Sites;

public sealed class SitesEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Lists_sites_with_floors_ordered_by_level()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);

        var sites = await client.GetFromJsonAsync<List<SiteDto>>(new Uri("/api/v1/sites", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(sites);
        Assert.Equal(2, sites.Count);
        var hq = sites.Single(s => s.Code == "CPH-HQ");
        Assert.Equal([0, 1, 2], hq.Floors.Select(f => f.Level));
        Assert.Equal("Europe/Copenhagen", hq.TimeZoneId);
    }

    [Fact]
    public async Task Area_details_include_level_and_owners()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid areaId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            areaId = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Areas.SingleAsync(a => a.Code == "CPH-SERVER", ct)).Id.Value;
        }

        using var client = _factory.CreateClientFor(DemoUser.Anna);
        var area = await client.GetFromJsonAsync<AreaDetailsDto>(new Uri($"/api/v1/areas/{areaId}", UriKind.Relative), ct);

        Assert.NotNull(area);
        Assert.Equal("Server Room", area.Name);
        Assert.Equal("High", area.SecurityLevel.Name);
        Assert.Equal("Both", area.SecurityLevel.ApproverChain);
        Assert.Equal(182, area.SecurityLevel.MaxDurationDays);
        Assert.Equal("Sofie Berg", Assert.Single(area.Owners).DisplayName);
    }

    [Fact]
    public async Task Unknown_area_returns_404_with_code()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClientFor(DemoUser.Anna);

        using var response = await client.GetAsync(new Uri($"/api/v1/areas/{Guid.NewGuid()}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("area.not_found", body.GetProperty("code").GetString());
    }
}
