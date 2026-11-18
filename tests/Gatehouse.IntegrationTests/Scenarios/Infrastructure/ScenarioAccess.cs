using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// Access through the product flow: the manager persona requests on behalf of a scenario person, the approvers decide in their
/// inbox (never the requester), and the grant is done only when every PACS confirmed it.
/// </summary>
public sealed class ScenarioAccess(ScenarioAppHostFixture fixture)
{
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, Guid> _areaIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _floorIds = new(StringComparer.Ordinal);

    /// <summary>Requests, approves and waits until every grant fulfillment of the entitlement is <c>Confirmed</c>; returns the entitlement id.</summary>
    public async Task<Guid> GrantAsync(ScenarioPerson person, ScenarioArea area, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var entitlementId = await RequestAndApproveAsync(person, area, from, to, ct);
        await WaitForFulfillmentsAsync(entitlementId, "Grant", "Confirmed", ConfirmTimeout, ct);
        return entitlementId;
    }

    /// <summary>Requests on behalf of the person and approves until <c>Approved</c>, without waiting for the PACS.</summary>
    public async Task<Guid> RequestAndApproveAsync(ScenarioPerson person, ScenarioArea area, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(area);
        var manager = await fixture.ClientAsync(ScenarioPersonas.Manager, ct);
        var areaId = await AreaIdAsync(area, ct);
        var request = await manager.PostAsync<AccessRequestDto>(
            "v1/access-requests", new CreateAccessRequestDto(person.PersonId, areaId, from, to, "M11 scenario access"), ct);
        await ApproveAsync(request.EntitlementId, manager.Email, ct);
        return request.EntitlementId;
    }

    /// <summary>
    /// The approver personas (except <paramref name="requesterEmail"/>) approve their inbox steps of the entitlement until it is
    /// <c>Approved</c>; also used for renewals the lifecycle opened (requester = the person).
    /// </summary>
    public async Task ApproveAsync(Guid entitlementId, string? requesterEmail, CancellationToken ct)
    {
        var manager = await fixture.ClientAsync(ScenarioPersonas.Manager, ct);
        for (var round = 0; round < 4; round++)
        {
            foreach (var approverEmail in ScenarioPersonas.Approvers.Where(a => a != requesterEmail))
            {
                var approver = await fixture.ClientAsync(approverEmail, ct);
                var inbox = await approver.GetAsync<List<ApprovalInboxItemDto>>("v1/approvals/inbox", ct);
                foreach (var item in inbox.Where(i => i.EntitlementId == entitlementId))
                {
                    await approver.PostAsync<System.Text.Json.JsonElement>($"v1/approvals/{item.StepId}/approve", new DecideStepDto("M11 scenario approval"), ct);
                }
            }

            var current = await manager.GetAsync<AccessRequestDto>($"v1/access-requests/{entitlementId}", ct);
            if (current.Status == "Approved")
            {
                return;
            }
        }

        throw new InvalidOperationException($"Access request {entitlementId} is not Approved after the approvers decided.");
    }

    /// <summary>Owner-style revoke as the given session (<c>POST /access-requests/{id}/revoke</c>).</summary>
    public static Task<AccessRequestDto> RevokeAsync(ScenarioClient actor, Guid entitlementId, CancellationToken ct) =>
        Post(actor, $"v1/access-requests/{entitlementId}/revoke", ct);

    /// <summary>Emergency revoke as the given session, normally security staff.</summary>
    public static Task<AccessRequestDto> EmergencyRevokeAsync(ScenarioClient actor, Guid entitlementId, CancellationToken ct) =>
        Post(actor, $"v1/access-requests/{entitlementId}/emergency-revoke", ct);

    /// <summary>Waits until the entitlement has at least one fulfillment of the kind and all of them have the status.</summary>
    public async Task<IReadOnlyList<FulfillmentRow>> WaitForFulfillmentsAsync(Guid entitlementId, string kind, string status, TimeSpan timeout, CancellationToken ct)
    {
        List<FulfillmentRow> rows = [];
        var reached = await PacsProbe.WaitUntilAsync(
            async c =>
            {
                rows = (await fixture.Db.FulfillmentsAsync(entitlementId, c)).Where(f => f.Kind == kind).ToList();
                return rows.Count > 0 && rows.All(f => f.Status == status);
            },
            timeout,
            ct);
        if (!reached)
        {
            var seen = string.Join(", ", rows.Select(f => f.Status));
            throw new TimeoutException($"{kind} fulfillments of {entitlementId} not all {status} within {timeout} (seen: [{seen}]).");
        }

        return rows;
    }

    public async Task<Guid> AreaIdAsync(ScenarioArea area, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (_areaIds.TryGetValue(area.Code, out var cached))
        {
            return cached;
        }

        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var sites = await admin.GetAsync<List<SiteDto>>("v1/sites", ct);
        var site = sites.Single(s => s.Code == area.SiteCode);
        foreach (var floor in site.Floors)
        {
            var map = await admin.GetAsync<FloorAccessMapDto>($"v1/floors/{floor.FloorId}/access-map", ct);
            if (map.Areas.FirstOrDefault(a => a.Code == area.Code) is { } found)
            {
                _areaIds[area.Code] = found.AreaId;
                _floorIds[area.Code] = floor.FloorId;
                return found.AreaId;
            }
        }

        throw new InvalidOperationException($"Area {area.Code} not found on site {area.SiteCode}.");
    }

    /// <summary>The floor the area is drawn on.</summary>
    public async Task<Guid> FloorIdAsync(ScenarioArea area, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        await AreaIdAsync(area, ct);
        return _floorIds[area.Code];
    }

    private static Task<AccessRequestDto> Post(ScenarioClient actor, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.PostAsync<AccessRequestDto>(path, new RevokeEntitlementDto("M11 scenario revoke"), ct);
    }
}
