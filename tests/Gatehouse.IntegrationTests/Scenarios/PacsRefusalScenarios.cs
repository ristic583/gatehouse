using System.Text.Json;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 9, 10 (improvements.md §A2, conventions.md §4.10): refused removals stay visible; tasks resolve only on a confirmed effect.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class PacsRefusalScenarios(ScenarioAppHostFixture fixture)
{
    /// <summary>Five delivery attempts with 5-40 s backoff (ConnectorCommand.MaxAttempts) before a refused command fails.</summary>
    private static readonly TimeSpan FailTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan EffectTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Refused_scheduled_removal_is_retried_and_visible_until_resolved()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.AarhusDock;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        var entitlement = await fixture.Access.GrantAsync(person, area, now, now.AddDays(7), ct);

        FulfillmentRow revoke;
        await using (await fixture.Pacs.CloudChaosAsync(failNext: 100_000, rateLimitNext: null, errorRate: null, rateLimitRate: null, ct))
        {
            await fixture.Clock.SetEntitlementWindowAsync(entitlement, now.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(-1), ct);
            await ScenarioClock.RunLifecycleAsync(admin, ct);
            revoke = (await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Revoke", "Failed", FailTimeout, ct)).Single();

            // Visible centrally while the PACS refuses: an open staff task about the revoke.
            var open = await PacsProbe.WaitUntilAsync(
                async c => (await fixture.Db.TasksForSubjectAsync(revoke.FulfillmentId, c)).Any(t => t.Status != "Resolved"), EffectTimeout, ct);
            Assert.True(open, "No open staff task for the refused revoke.");
            Assert.Contains(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == area.AccessLevelRef && a.Active);
        }

        using (var retry = await staff.PostAsync($"v1/fulfillments/{revoke.FulfillmentId}/retry", null, ct))
        {
            Assert.True(retry.IsSuccessStatusCode, $"retry: {(int)retry.StatusCode} {await retry.Content.ReadAsStringAsync(ct)}");
        }

        await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Revoke", "Confirmed", EffectTimeout, ct);
        Assert.DoesNotContain(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == area.AccessLevelRef && a.Active);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "revoke retried after the refusal");
        var resolved = await PacsProbe.WaitUntilAsync(
            async c => (await fixture.Db.TasksForSubjectAsync(revoke.FulfillmentId, c)).All(t => t.Status == "Resolved"), EffectTimeout, ct);
        Assert.True(resolved, "The staff task of the revoke is not resolved after the confirmed removal.");
    }

    [Fact]
    public async Task Failed_remove_from_pacs_remediation_keeps_task_open()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.AarhusDock;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);

        // A leftover in the PACS that Gatehouse never granted; reconciliation reports it as a finding with a staff task.
        await fixture.Pacs.AddCloudLeftoverAsync(person.PersonId.ToString(), person.CardNumber, area.AccessLevelRef, ct);
        var instance = await fixture.Mappings.InstanceAsync(area.PacsInstanceName, ct);
        string reportBody;
        using (var report = await staff.PostAsync("v1/reconciliation/reports", new { pacsInstanceId = instance.Id }, ct))
        {
            reportBody = await report.Content.ReadAsStringAsync(ct);
            Assert.True(report.IsSuccessStatusCode, $"reconciliation: {(int)report.StatusCode} {reportBody}");
        }

        Guid? taskId = null;
        var found = await PacsProbe.WaitUntilAsync(async c => (taskId = await fixture.Db.FindingTaskAsync(person.PersonId, c)) is not null, FailTimeout, ct);
        Assert.True(found, $"Reconciliation opened no finding task for the leftover assignment. {await ReportStateAsync(staff, reportBody, person.PersonId, ct)}");

        await using (await fixture.Pacs.CloudChaosAsync(failNext: 100_000, rateLimitNext: null, errorRate: null, rateLimitRate: null, ct))
        {
            await ResolveAsync(staff, taskId!.Value, ct);
            await Task.Delay(FailTimeout / 2, ct);
            Assert.Equal("InProgress", await fixture.Db.TaskStatusAsync(taskId.Value, ct));
            Assert.Contains(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == area.AccessLevelRef && a.Active);
        }

        await ResolveAsync(staff, taskId.Value, ct);
        var gone = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, c)).Any(a => a.GroupId == area.AccessLevelRef && a.Active), FailTimeout, ct);
        Assert.True(gone, "The leftover assignment is still in CloudPACS after the retried removal.");
        var resolved = await PacsProbe.WaitUntilAsync(async c => await fixture.Db.TaskStatusAsync(taskId.Value, c) == "Resolved", EffectTimeout, ct);
        Assert.True(resolved, "The finding task is not resolved after the confirmed removal.");
    }

    /// <summary>Report status, failure and this person's findings, so a missing finding task explains itself.</summary>
    private static async Task<string> ReportStateAsync(ScenarioClient staff, string reportBody, Guid personId, CancellationToken ct)
    {
        using var started = JsonDocument.Parse(reportBody);
        var reportId = started.RootElement.GetProperty("reportId").GetGuid();
        using var response = await staff.SendAsync(HttpMethod.Get, $"v1/reconciliation/reports/{reportId}", null, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return $"report {reportId}: {(int)response.StatusCode} {text}";
        }

        using var report = JsonDocument.Parse(text);
        var root = report.RootElement;
        var summary = root.GetProperty("summary");
        var mine = root.GetProperty("findings").EnumerateArray()
            .Where(f => f.TryGetProperty("personRef", out var r) && r.GetString() == personId.ToString())
            .Select(f => f.GetProperty("kind").GetString());
        return $"report {reportId}: status {summary.GetProperty("status")}, failure {root.GetProperty("failureCode")} {root.GetProperty("failureMessage")}, "
            + $"findings {summary.GetProperty("findingCount")}, compared {summary.GetProperty("comparedRecordCount")}, this person's findings [{string.Join(", ", mine)}], coverage {root.GetProperty("coverage")}";
    }

    private static async Task ResolveAsync(ScenarioClient staff, Guid taskId, CancellationToken ct)
    {
        using var response = await staff.PostAsync($"v1/tasks/{taskId}/resolve", new { action = "RemovedFromPacs", note = "M11 scenario 10" }, ct);
        Assert.True(response.IsSuccessStatusCode, $"resolve: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
    }
}
