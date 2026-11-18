using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Audit.Dtos;
using Gatehouse.Domain.Audit;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Audit;

public sealed class AuditEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Admin_and_tenant_wide_staff_can_verify()
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var user in new[] { DemoUser.Freja, DemoUser.Jonas })
        {
            using var client = _factory.CreateClientFor(user);
            var dto = await client.GetFromJsonAsync<AuditVerificationDto>(new Uri("/api/v1/audit/verify", UriKind.Relative), ct);
            Assert.NotNull(dto);
            Assert.True(dto.Verified);
        }
    }

    [Fact]
    public async Task Employee_gets_403()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await client.GetAsync(new Uri("/api/v1/audit/verify", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Entries_filter_by_subject()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        await RecordAsync("nordlys", subject);
        using var client = _factory.CreateClientFor(DemoUser.Freja);
        var list = await client.GetFromJsonAsync<List<AuditEntryDto>>(new Uri($"/api/v1/audit/entries?subjectType=Entitlement&subjectId={subject}", UriKind.Relative), ct);
        var entry = Assert.Single(list!);
        Assert.Equal("entitlement.requested", entry.Action);
        Assert.Equal(7, entry.Payload.GetProperty("n").GetInt32());
    }

    internal static async Task RecordAsync(GatehouseApiFactory factory, string tenant, string subject)
    {
        await using var scope = await factory.CreateTenantScopeAsync(tenant);
        scope.ServiceProvider.GetRequiredService<IAuditLog>()
            .Record(AuditActor.ForSystem("test"), "entitlement.requested", AuditSubjects.Entitlement, subject, new { n = 7 });
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(CancellationToken.None);
    }

    private Task RecordAsync(string tenant, string subject) => RecordAsync(_factory, tenant, subject);
}
