using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class AlarmEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<AlarmId> _created = [];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Lists_open_alarms_for_viewable_sites()
    {
        var ct = TestContext.Current.CancellationToken;
        var hq = await RaiseAsync(a => a.Code == "CPH-VAULT");
        var (warehouseSite, warehouse) = await RaiseInSiteAsync("AAR-WH");
        RoleAssignmentId assignmentId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = await db.Persons.SingleAsync(p => p.Email == "person-01@nordlys.test", ct);
            var assignment = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, warehouseSite).Value;
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            assignmentId = assignment.Id;
        }

        try
        {
            using var jonas = _api.CreateClientFor(DemoUser.Jonas);
            var all = await jonas.GetFromJsonAsync<List<AlarmDto>>(new Uri("/api/v1/live/alarms?status=open", UriKind.Relative), ApiJson.Options, ct);
            Assert.NotNull(all);
            Assert.Contains(all, a => a.AlarmId == hq.Value);
            Assert.Contains(all, a => a.AlarmId == warehouse.Value);

            using var scoped = _api.CreateClientFor("test-person-01", "person-01@nordlys.test", Tenant);
            var mine = await scoped.GetFromJsonAsync<List<AlarmDto>>(new Uri("/api/v1/live/alarms", UriKind.Relative), ApiJson.Options, ct);
            Assert.NotNull(mine);
            Assert.Contains(mine, a => a.AlarmId == warehouse.Value);
            Assert.DoesNotContain(mine, a => a.AlarmId == hq.Value);

            var acknowledged = await jonas.GetFromJsonAsync<List<AlarmDto>>(new Uri("/api/v1/live/alarms?status=acknowledged", UriKind.Relative), ApiJson.Options, ct);
            Assert.NotNull(acknowledged);
            Assert.DoesNotContain(acknowledged, a => a.AlarmId == hq.Value);

            using var anna = _api.CreateClientFor(DemoUser.Anna);
            using var forbidden = await anna.GetAsync(new Uri("/api/v1/live/alarms", UriKind.Relative), ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        finally
        {
            await using var scope = await _api.CreateTenantScopeAsync(Tenant);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.RoleAssignments.Where(r => r.Id == assignmentId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Acknowledge_requires_comment()
    {
        var ct = TestContext.Current.CancellationToken;
        var alarm = await RaiseAsync(a => a.Code == "CPH-VAULT");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(AckUri(alarm), new { comment = "  " }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("alarm.comment_required", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.True(await IsOpenAsync(alarm));
    }

    [Fact]
    public async Task Acknowledge_is_audited_and_published()
    {
        var ct = TestContext.Current.CancellationToken;
        var alarm = await RaiseAsync(a => a.Code == "CPH-VAULT");
        var published = new TaskCompletionSource<LiveUpdateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _api.Services.GetRequiredService<ILiveUpdateSubscriber>().SubscribeAsync((tenant, update) =>
        {
            if (tenant == Tenant && update.Kind == LiveUpdateKind.Alarm && update.Alarm?.AlarmId == alarm.Value)
            {
                published.TrySetResult(update);
            }

            return Task.CompletedTask;
        }, ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(AckUri(alarm), new { comment = "Checked on site, door closed" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AlarmDto>(ApiJson.Options, ct);
        Assert.NotNull(dto);
        Assert.NotNull(dto.AcknowledgedAt);
        Assert.Equal("Checked on site, door closed", dto.Comment);
        Assert.False(string.IsNullOrEmpty(dto.AcknowledgedByName));
        var update = await published.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.NotNull(update.Alarm?.AcknowledgedAt);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "alarm.acknowledged" && a.SubjectId == alarm.Value.ToString(), ct);
        Assert.Equal("Alarm", entry.SubjectType);
        Assert.Contains("Checked on site, door closed", entry.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_acknowledge_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        var alarm = await RaiseAsync(a => a.Code == "CPH-VAULT");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var first = await jonas.PostAsJsonAsync(AckUri(alarm), new { comment = "Seen" }, ct);
        using var second = await jonas.PostAsJsonAsync(AckUri(alarm), new { comment = "Seen again" }, ct);
        using var missing = await jonas.PostAsJsonAsync(AckUri(new AlarmId(Guid.CreateVersion7())), new { comment = "x" }, ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("alarm.already_acknowledged", await second.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static Uri AckUri(AlarmId id) => new($"/api/v1/live/alarms/{id.Value}/acknowledge", UriKind.Relative);

    private async Task<bool> IsOpenAsync(AlarmId id)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Alarms.AsNoTracking().SingleAsync(a => a.Id == id, TestContext.Current.CancellationToken)).IsOpen;
    }

    private async Task<AlarmId> RaiseAsync(System.Linq.Expressions.Expression<Func<Area, bool>> area)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var areaId = await db.Areas.AsNoTracking().Where(area).Select(a => a.Id).FirstAsync(ct);
        var doorId = await db.Doors.AsNoTracking().Where(d => d.AreaId == areaId).Select(d => d.Id).FirstAsync(ct);
        var alarm = Alarm.Raise(doorId, AlarmKind.HeldOpen, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        db.Alarms.Add(alarm);
        await db.SaveChangesAsync(ct);
        _created.Add(alarm.Id);
        return alarm.Id;
    }

    private async Task<(SiteId Site, AlarmId Alarm)> RaiseInSiteAsync(string siteCode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var site = await db.Sites.AsNoTracking().SingleAsync(s => s.Code == siteCode, ct);
        var floorIds = await db.Floors.AsNoTracking().Where(f => f.SiteId == site.Id).Select(f => f.Id).ToListAsync(ct);
        return (site.Id, await RaiseAsync(a => floorIds.Contains(a.FloorId)));
    }
}
