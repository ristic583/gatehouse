using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.People.HrSync;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>HR sync over a temp feed directory; the people the sync creates are deleted afterwards.</summary>
public sealed class HrSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Sync = new("/api/v1/admin/hr-sync", UriKind.Relative);
    private static readonly JsonSerializerOptions FeedJson = new(JsonSerializerDefaults.Web);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly string _directory = Directory.CreateTempSubdirectory("gh-hr-feed-").FullName;
    private readonly List<string> _emails = [];
    private LifecycleArrange _arrange = null!;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _api.Services.GetRequiredService<IConfiguration>()["HrSync:FeedDirectory"] = _directory;
        _arrange = new LifecycleArrange(_api);
        await _arrange.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Sync_creates_new_employees_with_eligibility()
    {
        var ct = TestContext.Current.CancellationToken;
        var lead = NewEmail();
        var hire = NewEmail();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await WriteFeedAsync(ct, Record(hire, lead, start, null), Record(lead, DemoUser.Mikkel.Email, start, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = await RunAsync(ct);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Skipped);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var people = await db.Persons.AsNoTracking().Where(p => p.Email == hire || p.Email == lead || p.Email == DemoUser.Mikkel.Email).ToDictionaryAsync(p => p.Email, ct);
        Assert.Equal(PersonSource.Authoritative, people[hire].Source);
        Assert.Equal(PersonType.Employee, people[hire].Type);
        Assert.Equal(start, people[hire].Eligibility.Start);
        Assert.Null(people[hire].Eligibility.End);
        Assert.Equal(people[lead].Id, people[hire].ManagerId);
        Assert.Equal(people[DemoUser.Mikkel.Email].Id, people[lead].ManagerId);
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), people[lead].Eligibility.End);
    }

    [Fact]
    public async Task Sync_updates_end_date_and_audits_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = NewEmail();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2029, 6, 30, 0, 0, 0, TimeSpan.Zero);
        await WriteFeedAsync(ct, Record(email, DemoUser.Mikkel.Email, start, null));
        await RunAsync(ct);
        await WriteFeedAsync(ct, Record(email, DemoUser.Mikkel.Email, start, end));

        var result = await RunAsync(ct);

        Assert.Equal(1, result.Updated);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == email, ct);
        Assert.Equal(end, person.Eligibility.End);
        var audits = await db.AuditEntries.AsNoTracking().Where(a => a.Action == "person.eligibility_changed" && a.SubjectId == person.Id.Value.ToString()).ToListAsync(ct);
        Assert.Equal(2, audits.Count);
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "hr.sync_completed", ct));
    }

    [Fact]
    public async Task Sync_never_touches_local_people()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var localId = await _arrange.ExternalAsync(now.AddDays(-5), now.AddDays(30), ct);
        string localEmail;
        await using (var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant))
        {
            localEmail = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking().SingleAsync(p => p.Id == localId, ct)).Email;
        }

        await WriteFeedAsync(ct, new HrFeedRecord(localEmail, "Renamed", "ByHr", null, now.AddYears(-1), null, "Europe/Copenhagen"));

        var result = await RunAsync(ct);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Created + result.Updated);
        await using var check = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var local = await check.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking().SingleAsync(p => p.Id == localId, ct);
        Assert.Equal(PersonSource.Local, local.Source);
        Assert.Equal(PersonType.External, local.Type);
        Assert.Equal("Life", local.GivenName);
    }

    [Fact]
    public async Task Sync_without_feed_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await freja.PostAsync(Sync, null, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("hr_sync.feed_unavailable", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_sync_is_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = NewEmail();
        var b = NewEmail();
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        await WriteFeedAsync(ct, Record(a, DemoUser.Mikkel.Email, start, null), Record(b, a, start, null));

        var first = await RunAsync(ct);
        var second = await RunAsync(ct);

        Assert.Equal(2, first.Created);
        Assert.Equal(new HrSyncResult(0, 0, 2, 0), second);
    }

    private string NewEmail()
    {
        var email = $"hr-{Guid.NewGuid():N}@nordlys.test";
        _emails.Add(email);
        return email;
    }

    private static HrFeedRecord Record(string email, string? manager, DateTimeOffset start, DateTimeOffset? end) =>
        new(email, "Hr", "Hire", manager, start, end, "Europe/Copenhagen");

    private async Task WriteFeedAsync(CancellationToken ct, params HrFeedRecord[] records)
    {
        await using var stream = File.Create(Path.Combine(_directory, LifecycleArrange.Tenant + ".json"));
        await JsonSerializer.SerializeAsync(stream, records, FeedJson, ct);
    }

    private async Task<HrSyncResult> RunAsync(CancellationToken ct)
    {
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var response = await freja.PostAsync(Sync, null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HrSyncResult>(ApiJson.Options, ct))!;
    }
}
