using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.IntegrationTests.Infrastructure;
using static Gatehouse.IntegrationTests.Access.AccessRequestEndpointTests;
using static Gatehouse.IntegrationTests.Access.ApprovalEndpointTests;

namespace Gatehouse.IntegrationTests.Authorization;

/// <summary>One test per row of authorization.md §6 that has a resource check: allowed and forbidden callers.</summary>
public sealed class AuthorizationMatrixTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Create_request_requires_self_manager_or_sponsor()
    {
        var anna = await PersonIdAsync(_factory, "nordlys", DemoUser.Anna.Email);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var allowed = await PostRequestAsync(_factory, mikkel, "nordlys", "AAR-OFFICE", anna);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var forbidden = await PostRequestAsync(_factory, sofie, "nordlys", "AAR-OFFICE", anna);
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "request.forbidden_for_subject");
    }

    [Fact]
    public async Task List_requests_shows_only_mine()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);

        var annas = await anna.GetFromJsonAsync<List<AccessRequestDto>>(Requests, TestContext.Current.CancellationToken);
        var sofies = await sofie.GetFromJsonAsync<List<AccessRequestDto>>(Requests, TestContext.Current.CancellationToken);

        Assert.Equal(5, annas!.Count);
        Assert.Empty(sofies!);
    }

    [Fact]
    public async Task Get_request_requires_view_entitlement()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var created = await PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await CreatedAsync(created);
        var url = new Uri($"/api/v1/access-requests/{request.EntitlementId}", UriKind.Relative);

        foreach (var allowedUser in new[] { DemoUser.Anna, DemoUser.Mikkel, DemoUser.Sofie, DemoUser.Freja, DemoUser.Jonas })
        {
            using var client = _factory.CreateClientFor(allowedUser);
            using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{allowedUser.Username} should see the request");
        }

        foreach (var forbiddenUser in new[] { DemoUser.Erik, DemoUser.Oliver })
        {
            using var client = _factory.CreateClientFor(forbiddenUser);
            using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{forbiddenUser.Username} should not see the request");
        }
    }

    [Fact]
    public async Task Inbox_shows_only_steps_awaiting_me()
    {
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var freja = _factory.CreateClientFor(DemoUser.Freja);

        Assert.Equal("Lab", Assert.Single(await InboxAsync(sofie)).Area.Name);
        Assert.Equal("Vault", Assert.Single(await InboxAsync(mikkel)).Area.Name);
        Assert.Empty(await InboxAsync(freja));
    }

    [Fact]
    public async Task Approve_requires_approver_and_not_self()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        var stepId = await SeededLabStepAsync(anna);

        foreach (var (user, code) in new[] { (DemoUser.Anna, "approval.self_approval_forbidden"), (DemoUser.Freja, "approval.not_an_approver"), (DemoUser.Jonas, "approval.not_an_approver"), (DemoUser.Mikkel, "approval.not_an_approver") })
        {
            using var client = _factory.CreateClientFor(user);
            using var forbidden = await DecideAsync(client, stepId, "approve");
            await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, code);
        }

        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var allowed = await DecideAsync(sofie, stepId, "approve");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Reject_requires_approver_and_not_self()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        var mine = await anna.GetFromJsonAsync<List<AccessRequestDto>>(Requests, TestContext.Current.CancellationToken);
        var vaultStep = mine!.Single(r => r.Area.Name == "Vault").Steps[0].StepId;

        foreach (var (user, code) in new[] { (DemoUser.Anna, "approval.self_approval_forbidden"), (DemoUser.Sofie, "approval.not_an_approver"), (DemoUser.Freja, "approval.not_an_approver") })
        {
            using var client = _factory.CreateClientFor(user);
            using var forbidden = await DecideAsync(client, vaultStep, "reject", "no");
            await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, code);
        }

        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var allowed = await DecideAsync(mikkel, vaultStep, "reject", "Not needed");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
