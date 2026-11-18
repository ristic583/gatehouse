using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Dashboard;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Dashboard;

/// <summary>Business dashboard (M9-11); added alarms and the staff role are removed afterwards.</summary>
public sealed class DashboardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Dashboard = new("/api/v1/dashboard", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<AlarmId> _alarms = [];
    private RoleAssignmentId? _role;

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Counts_match_seeded_data()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddAlarmAsync("CPH-LAB", ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        var dashboard = await jonas.GetFromJsonAsync<DashboardDto>(Dashboard, ApiJson.Options, ct);

        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(await db.Entitlements.CountAsync(e => e.Status == EntitlementStatus.PendingApproval, ct), dashboard!.PendingApprovals);
        Assert.Equal(await db.Alarms.CountAsync(a => a.AcknowledgedAt == null, ct), dashboard.OpenAlarms);
        Assert.Equal(await db.StaffTasks.CountAsync(t => t.Status != StaffTaskStatus.Resolved, ct), dashboard.OpenTasksByKind.Values.Sum());
        Assert.Equal(await db.PacsInstances.CountAsync(p => p.FulfillmentMode == Domain.Fulfillment.FulfillmentMode.Connector, ct), dashboard.Reconciliation.Count);
        Assert.Equal(dashboard.Reconciliation.Count, dashboard.Connectors.Online + dashboard.Connectors.Offline + dashboard.Connectors.NeverSeen);
    }

    [Fact]
    public async Task Site_scoped_staff_sees_scoped_alarms()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddAlarmAsync("CPH-LAB", ct);
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = await db.Persons.SingleAsync(p => p.Email == "person-03@nordlys.test", ct);
            var site = await db.Sites.SingleAsync(s => s.Code == "AAR-WH", ct);
            var role = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, site.Id).Value;
            db.RoleAssignments.Add(role);
            await db.SaveChangesAsync(ct);
            _role = role.Id;
        }

        using var scoped = _api.CreateClientFor("test-person-03", "person-03@nordlys.test", "nordlys");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var mine = await scoped.GetFromJsonAsync<DashboardDto>(Dashboard, ApiJson.Options, ct);
        var all = await jonas.GetFromJsonAsync<DashboardDto>(Dashboard, ApiJson.Options, ct);

        Assert.True(mine!.OpenAlarms < all!.OpenAlarms);
    }

    [Fact]
    public async Task Employee_gets_403()
    {
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await anna.GetAsync(Dashboard, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task AddAlarmAsync(string areaCode, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var door = await db.Doors.AsNoTracking().SingleAsync(d => d.PacsDoorRef == "D-" + areaCode, ct);
        await db.Alarms.Where(a => a.DoorId == door.Id && a.AcknowledgedAt == null).ExecuteDeleteAsync(ct);
        var alarm = Alarm.Raise(door.Id, AlarmKind.Forced, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        alarm.ClearDomainEvents();
        db.Alarms.Add(alarm);
        _alarms.Add(alarm.Id);
        await db.SaveChangesAsync(ct);
    }
}
