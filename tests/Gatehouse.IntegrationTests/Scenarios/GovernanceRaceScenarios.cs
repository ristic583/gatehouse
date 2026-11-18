using System.Net;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 16, 18 (improvements.md §A2, conventions.md §4.7): cross-row invariants under concurrency; suspension cuts off and restores.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class GovernanceRaceScenarios(ScenarioAppHostFixture fixture)
{
    private const string Tenant = "nordlys";

    /// <summary>The agents back off up to 5 min after refusals (connector-protocol.md); plus margin.</summary>
    private static readonly TimeSpan AgentReturnTimeout = TimeSpan.FromSeconds(330);

    [Fact]
    public async Task Concurrent_requests_for_same_area_create_one_pending_request()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var manager = await fixture.ClientAsync(ScenarioPersonas.Manager, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, manager, ct);
        var areaId = await fixture.Access.AreaIdAsync(ScenarioAreas.HqLab, ct);
        var now = DateTimeOffset.UtcNow;
        var body = new CreateAccessRequestDto(person.PersonId, areaId, now, now.AddDays(7), "M11 scenario 16");

        var responses = await Task.WhenAll(manager.PostAsync("v1/access-requests", body, ct), manager.PostAsync("v1/access-requests", body, ct));
        try
        {
            var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToList();
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
            var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains("request.duplicate", await conflict.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            Assert.Equal(1, await fixture.Db.CountEntitlementsAsync(person.PersonId, areaId, "PendingApproval", ct));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Suspended_tenant_refuses_agents_and_sign_in_and_reactivation_restores_both()
    {
        // Longer than the 6 min per-test budget: the agents' back-off after the refusals alone may take 5.5 min.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(9));
        var ct = cts.Token;
        using var platform = await PlatformClient.SignInAsync(fixture, ct);
        try
        {
            await PostOkAsync(platform, $"v1/platform/tenants/{Tenant}/suspend", new { reason = "M11 scenario 18" }, ct);
            var suspendedAt = DateTimeOffset.UtcNow;
            await Task.Delay(TenantConnectionResolver.CacheDuration, ct);
            var refusedFrom = DateTimeOffset.UtcNow;

            using (var user = new ScenarioClient(fixture.WebUrl, ScenarioPersonas.SecurityStaff, await fixture.ParameterAsync("demo-user-password", ct)))
            {
                await Assert.ThrowsAnyAsync<Exception>(() => user.SignInAsync(ct));
            }

            var refused = await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Gateway.PostDoorEventsAsync(
                [new DoorEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow, DoorEventType.DoorClosed, ScenarioAreas.HqLab.DoorRef, null, null, null, null)], ct));
            Assert.Contains("403", refused.Message, StringComparison.Ordinal);

            // No heartbeat is accepted for either instance while suspended.
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            foreach (var instance in new[] { AgentControl.PacsInstanceOf(AgentControl.HqAgent), AgentControl.PacsInstanceOf(AgentControl.AarhusAgent) })
            {
                var last = await fixture.Db.LastHeartbeatAsync(instance, ct);
                Assert.True(last is null || last < refusedFrom, $"{instance} heartbeat accepted at {last} (suspended at {suspendedAt:O}).");
            }

            await PostOkAsync(platform, $"v1/platform/tenants/{Tenant}/reactivate", null, ct);
            var reactivatedAt = DateTimeOffset.UtcNow;
            foreach (var instance in new[] { AgentControl.PacsInstanceOf(AgentControl.HqAgent), AgentControl.PacsInstanceOf(AgentControl.AarhusAgent) })
            {
                await fixture.Db.WaitForHeartbeatAsync(instance, reactivatedAt, AgentReturnTimeout, ct);
            }

            await fixture.ForgetSessionsAsync();
            var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
            Assert.Equal("nordlys", (await staff.GetAsync<Gatehouse.Application.Identity.MeDto>("v1/me", ct)).TenantId);
        }
        finally
        {
            using (var restore = await platform.PostAsync($"v1/platform/tenants/{Tenant}/reactivate", null, CancellationToken.None))
            {
                // 200 when it was still suspended, 409 when the test already reactivated it.
            }

            await fixture.ForgetSessionsAsync();
        }
    }

    private static async Task PostOkAsync(ScenarioClient client, string path, object? body, CancellationToken ct)
    {
        using var response = await client.PostAsync(path, body, ct);
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
    }
}
