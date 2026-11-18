using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Access;

public sealed class AccessRequestEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    public static readonly Uri Requests = new("/api/v1/access-requests", UriKind.Relative);

    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    public static async Task<Guid> AreaIdAsync(GatehouseApiFactory factory, string tenant, string code)
    {
        await using var scope = await factory.CreateTenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Areas.SingleAsync(a => a.Code == code, TestContext.Current.CancellationToken)).Id.Value;
    }

    public static async Task<Guid> PersonIdAsync(GatehouseApiFactory factory, string tenant, string email)
    {
        await using var scope = await factory.CreateTenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Persons.SingleAsync(p => p.Email == email, TestContext.Current.CancellationToken)).Id.Value;
    }

    public static async Task<HttpResponseMessage> PostRequestAsync(
        GatehouseApiFactory factory, HttpClient client, string tenant, string areaCode, Guid? personId = null, int startDays = 1, int endDays = 30)
    {
        var areaId = await AreaIdAsync(factory, tenant, areaCode);
        var now = DateTimeOffset.UtcNow;
        return await client.PostAsJsonAsync(
            Requests,
            new CreateAccessRequestDto(personId, areaId, now.AddDays(startDays), now.AddDays(endDays), "Needed for the infrastructure project"),
            TestContext.Current.CancellationToken);
    }

    public static async Task<AccessRequestDto> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AccessRequestDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Equal($"/api/v1/access-requests/{dto.EntitlementId}", response.Headers.Location?.OriginalString);
        return dto;
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_create_one_pending_request()
    {
        var ct = TestContext.Current.CancellationToken;
        using var first = _factory.CreateClientFor(DemoUser.Anna);
        using var second = _factory.CreateClientFor(DemoUser.Anna);

        var responses = await Task.WhenAll(
            PostRequestAsync(_factory, first, "nordlys", "CPH-SERVER"),
            PostRequestAsync(_factory, second, "nordlys", "CPH-SERVER"));

        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var areaId = await AreaIdAsync(_factory, "nordlys", "CPH-SERVER");
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, ct);
        Assert.Equal(1, await db.Entitlements.CountAsync(
            e => e.PersonId == anna.Id && e.AreaId == new Gatehouse.Domain.Sites.AreaId(areaId) && e.Status == Gatehouse.Domain.Access.EntitlementStatus.PendingApproval, ct));
    }

    [Fact]
    public async Task Pending_request_index_rejects_a_second_open_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var areaId = new Gatehouse.Domain.Sites.AreaId(await AreaIdAsync(_factory, "nordlys", "CPH-SERVER"));
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, ct);
        var now = DateTimeOffset.UtcNow;
        Gatehouse.Domain.Access.Entitlement Pending() => Gatehouse.Domain.Access.Entitlement.Create(anna.Id, areaId, anna.Id, now.AddDays(1), now.AddDays(5), now.AddDays(5),
            "Index test request", [new Gatehouse.Domain.Access.StepPlan(Gatehouse.Domain.Access.ApproverKind.AreaOwner, null)], now);

        db.Entitlements.Add(Pending());
        await db.SaveChangesAsync(ct);
        db.Entitlements.Add(Pending());

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Employee_requests_server_room_and_gets_two_steps()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-SERVER");
        var dto = await CreatedAsync(response);

        Assert.Equal("PendingApproval", dto.Status);
        Assert.Collection(
            dto.Steps,
            s => Assert.Equal(("Manager", "Pending", "Mikkel Holm"), (s.ApproverKind, s.Status, s.AssignedApprover?.DisplayName)),
            s => Assert.Equal(("AreaOwner", "Waiting"), (s.ApproverKind, s.Status)));
        Assert.Equal("Anna Larsen", dto.Subject.DisplayName);
        Assert.Equal("HQ Copenhagen", dto.Area.SiteName);
        Assert.Null(dto.FulfillmentStatus);
    }

    [Fact]
    public async Task Low_area_routes_to_manager_only()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-OFFICE-0");
        var dto = await CreatedAsync(response);

        Assert.Equal("Manager", Assert.Single(dto.Steps).ApproverKind);
    }

    [Fact]
    public async Task Medium_area_routes_to_area_owner_only()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "AAR-DOCK");
        var dto = await CreatedAsync(response);

        var step = Assert.Single(dto.Steps);
        Assert.Equal("AreaOwner", step.ApproverKind);
        Assert.Null(step.AssignedApprover);
    }

    [Fact]
    public async Task Request_beyond_eligibility_is_capped()
    {
        using var client = _factory.CreateClientFor(DemoUser.Oliver);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-OFFICE-0", endDays: 300);
        var dto = await CreatedAsync(response);

        Assert.True(dto.CappedToEligibility);
        Assert.True(dto.EffectiveEnd < dto.RequestedEnd);
        Assert.Equal("Mikkel Holm", Assert.Single(dto.Steps).AssignedApprover?.DisplayName);
    }

    [Fact]
    public async Task Request_exceeding_max_duration_is_rejected_with_code()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-SERVER", endDays: 200);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.exceeds_max_duration");
    }

    [Fact]
    public async Task Request_for_unmapped_area_is_rejected_with_code()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-PLANT");
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.area_not_mapped");
    }

    [Fact]
    public async Task Duplicate_request_is_409()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-LAB");
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "request.duplicate");
    }

    [Fact]
    public async Task Employee_cannot_request_for_colleague()
    {
        var colleague = await PersonIdAsync(_factory, "nordlys", "person-01@nordlys.test");
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-OFFICE-0", colleague);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "request.forbidden_for_subject");
    }

    [Fact]
    public async Task Manager_can_request_for_report()
    {
        var anna = await PersonIdAsync(_factory, "nordlys", DemoUser.Anna.Email);
        using var client = _factory.CreateClientFor(DemoUser.Mikkel);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-OFFICE-0", anna);
        var dto = await CreatedAsync(response);

        Assert.Equal("Mikkel Holm", dto.RequestedBy.DisplayName);
        Assert.Equal("Erik Møller", Assert.Single(dto.Steps).AssignedApprover?.DisplayName);
    }

    [Fact]
    public async Task Sponsor_can_request_for_external()
    {
        var oliver = await PersonIdAsync(_factory, "nordlys", DemoUser.Oliver.Email);
        using var client = _factory.CreateClientFor(DemoUser.Mikkel);
        using var response = await PostRequestAsync(_factory, client, "nordlys", "AAR-DOCK", oliver);
        var dto = await CreatedAsync(response);

        Assert.Equal("Oliver Kruse", dto.Subject.DisplayName);
    }

    [Fact]
    public async Task Request_appears_as_pending_on_map()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);
        using (var response = await PostRequestAsync(_factory, client, "nordlys", "CPH-SERVER"))
        {
            await CreatedAsync(response);
        }

        var floorId = await AccessMapEndpointTests.FloorIdAsync(_factory, "nordlys", "CPH-HQ", 2);
        var map = await client.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), TestContext.Current.CancellationToken);
        var server = map!.Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal(("Pending", "AwaitingApproval"), (server.State, server.Reason));

        var mine = await client.GetFromJsonAsync<List<AccessRequestDto>>(Requests, TestContext.Current.CancellationToken);
        Assert.Equal("Server Room", mine![0].Area.Name);
    }

    [Fact]
    public async Task Other_employee_cannot_read_request()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var dto = await CreatedAsync(response);

        using var colleague = _factory.CreateClientFor("test-person-01", "person-01@nordlys.test", "nordlys");
        using var read = await colleague.GetAsync(new Uri($"/api/v1/access-requests/{dto.EntitlementId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        using var own = await anna.GetAsync(new Uri($"/api/v1/access-requests/{dto.EntitlementId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }
}
