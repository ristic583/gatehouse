using System.Net;
using System.Net.Http.Json;
using System.Text;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gatehouse.IntegrationTests.Access.AccessRequestEndpointTests;
using static Gatehouse.IntegrationTests.Access.ApprovalEndpointTests;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>Security regression (M11, area 2): role and self-approval rules of authorization.md §2 hold on the HTTP surface.</summary>
public sealed class RoleBoundaryTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Generated guest of the Bricks seed (person 53, sponsored by Mia).</summary>
    private const string GuestEmail = "person-53@bricks.test";

    private static readonly HashSet<string> StaffOrAdminPolicies =
        [Policies.TenantAdmin, Policies.SecurityStaff, Policies.StaffConsole, Policies.AuditReader];

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Employee_and_guest_get_403_on_every_staff_or_admin_route()
    {
        var routes = ApiRouteCatalog.Routes(_api.Services)
            .Where(r => r.Policies.Any(StaffOrAdminPolicies.Contains) && !r.Pattern.StartsWith("/api/v1/platform", StringComparison.Ordinal))
            .ToList();
        using var employee = _api.CreateClientFor(DemoUser.Lena);
        using var guest = _api.CreateClientFor("test-guest-53", GuestEmail, "bricks");
        var failures = new List<string>();

        foreach (var route in routes)
        {
            var url = route.Url(p => route.GuidParameters.Contains(p) ? Guid.CreateVersion7().ToString() : "x");
            foreach (var (name, client) in new[] { ("employee", employee), ("guest", guest) })
            {
                using var message = new HttpRequestMessage(new HttpMethod(route.Method), new Uri(url, UriKind.Relative));
                if (!route.IsRead && route.Method != "DELETE")
                {
                    message.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{name} {route}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(routes.Count >= 40, $"Only {routes.Count} staff/admin routes found.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Sole_area_owner_cannot_request_own_area_for_self_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        using var clara = _api.CreateClientFor(DemoUser.Clara);

        using var created = await PostRequestAsync(_api, clara, "bricks", "BER-LAB");

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("approval.no_eligible_approver", await created.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_and_staff_cannot_approve_by_role()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        var pending = (await lena.GetFromJsonAsync<List<AccessRequestDto>>(Requests, ct))!.Single(r => r.Area.Name == "Lab");

        foreach (var user in new[] { DemoUser.Mia, DemoUser.Paul, DemoUser.Lena })
        {
            using var client = _api.CreateClientFor(user);
            using var response = await DecideAsync(client, pending.Steps[0].StepId, "approve");
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{user.Username}: {(int)response.StatusCode}");
        }

        var after = await lena.GetFromJsonAsync<AccessRequestDto>(new Uri($"/api/v1/access-requests/{pending.EntitlementId}", UriKind.Relative), ct);
        Assert.Equal("PendingApproval", after!.Status.ToString());
    }

    [Fact]
    public async Task Requester_for_a_report_cannot_approve_that_request()
    {
        var lena = await PersonIdAsync(_api, "bricks", DemoUser.Lena.Email);
        using var max = _api.CreateClientFor(DemoUser.Max);
        using var created = await PostRequestAsync(_api, max, "bricks", "BER-SERVER", lena);
        var request = await CreatedAsync(created);

        foreach (var step in request.Steps)
        {
            using var response = await DecideAsync(max, step.StepId, "approve");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var lenaClient = _api.CreateClientFor(DemoUser.Lena);
        using var self = await DecideAsync(lenaClient, request.Steps[0].StepId, "approve");
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
    }

    [Fact]
    public async Task Door_commands_require_security_staff()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await Access.AccessMapEndpointTests.FloorIdAsync(_api, "bricks", "BER-OFF", 0);
        var body = new { kind = "LockdownStart", scope = "Floor", scopeId = floorId, reason = "Drill" };
        using var guest = _api.CreateClientFor("test-guest-53", GuestEmail, "bricks");
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        using var jan = _api.CreateClientFor(DemoUser.Jan);

        foreach (var client in new[] { lena, mia, jan, guest })
        {
            using var response = await client.PostAsJsonAsync(new Uri("/api/v1/live/door-commands", UriKind.Relative), body, ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var paul = _api.CreateClientFor(DemoUser.Paul);
        using var allowed = await paul.PostAsJsonAsync(new Uri("/api/v1/live/door-commands", UriKind.Relative), body, ct);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
    }

    [Fact]
    public async Task Card_actions_require_the_right_role()
    {
        var ct = TestContext.Current.CancellationToken;
        var (guestId, guestEmail) = await NewPersonAsync(Domain.People.PersonType.Guest, ct);
        var (tomId, _) = await NewPersonAsync(Domain.People.PersonType.External, ct);
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        using var guest = _api.CreateClientFor("test-" + guestEmail, guestEmail, "bricks");
        var guestCard = await AssignAsync(mia, guestId, NewNumber(), ct);
        var tomCard = await AssignAsync(mia, tomId, NewNumber(), ct);

        using var guestActivatesOwn = await guest.PostAsync(new Uri($"/api/v1/cards/{guestCard}/activate", UriKind.Relative), null, ct);
        using var strangerActivates = await lena.PostAsync(new Uri($"/api/v1/cards/{tomCard}/activate", UriKind.Relative), null, ct);
        using var employeeDeactivates = await lena.PostAsJsonAsync(new Uri($"/api/v1/cards/{tomCard}/deactivate", UriKind.Relative), new { reason = "Lost" }, ct);
        using var employeeIssues = await lena.PostAsJsonAsync(new Uri($"/api/v1/people/{tomId}/cards", UriKind.Relative), new { cardNumber = NewNumber() }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, guestActivatesOwn.StatusCode);
        Assert.True(strangerActivates.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"activate {(int)strangerActivates.StatusCode}");
        Assert.True(employeeDeactivates.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"deactivate {(int)employeeDeactivates.StatusCode}");
        Assert.True(employeeIssues.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"issue {(int)employeeIssues.StatusCode}");
        await using var scope = await _api.CreateTenantScopeAsync("bricks");
        var states = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Cards.AsNoTracking()
            .Where(c => c.PersonId == new Domain.People.PersonId(guestId) || c.PersonId == new Domain.People.PersonId(tomId))
            .Select(c => c.State).ToListAsync(ct);
        Assert.Equal(2, states.Count);
        Assert.All(states, s => Assert.Equal(Domain.Credentials.CardState.Assigned, s));
    }

    private static string NewNumber() => Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fresh person without a card, sponsored by Max (so Lena is a stranger to them).</summary>
    private async Task<(Guid Id, string Email)> NewPersonAsync(Domain.People.PersonType type, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("bricks");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var max = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Max.Email, ct);
        var email = $"sec-{Guid.NewGuid():N}@bricks.test";
        var eligibility = Domain.People.Eligibility.Create(now.AddDays(-1), now.AddDays(5), "Europe/Berlin").Value;
        var person = Domain.People.Person.Create(Domain.People.PersonSource.Local, type, "Sec", "Test", email, eligibility, null, max, now).Value;
        person.ClearDomainEvents();
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return (person.Id.Value, email);
    }

    private static async Task<Guid> AssignAsync(HttpClient admin, Guid personId, string number, CancellationToken ct)
    {
        using var response = await admin.PostAsJsonAsync(new Uri($"/api/v1/people/{personId}/cards", UriKind.Relative), new { cardNumber = number }, ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        return (await response.Content.ReadFromJsonAsync<CardDto>(ApiJson.Options, ct))!.CardId;
    }
}
