using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>Admin lifecycle routes; the changed nordlys setting row is removed afterwards.</summary>
public sealed class LifecycleSettingsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri Settings = new("/api/v1/admin/lifecycle/settings", UriKind.Relative);
    private static readonly Uri Run = new("/api/v1/admin/lifecycle/run", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Settings_change_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        var before = await freja.GetFromJsonAsync<LifecycleSettingsDto>(Settings, ApiJson.Options, ct);
        using var changed = await freja.PutAsJsonAsync(Settings, new { key = LifecycleSettings.RenewalLeadDaysKey, days = 21 }, ct);
        using var invalid = await freja.PutAsJsonAsync(Settings, new { key = "lifecycle.unknown", days = 5 }, ct);

        Assert.Equal(LifecycleSettings.Defaults.RenewalLeadDays, before!.RenewalLeadDays);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(21, (await changed.Content.ReadFromJsonAsync<LifecycleSettingsDto>(ApiJson.Options, ct))!.RenewalLeadDays);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("lifecycle.invalid_setting", await invalid.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.AsNoTracking().Where(a => a.Action == "lifecycle.settings_changed" && a.SubjectId == Tenant).OrderByDescending(a => a.Sequence).FirstAsync(ct);
        Assert.Contains("21", entry.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_admin_cannot_run_lifecycle()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var employee = await anna.PostAsync(Run, null, ct);
        using var staff = await jonas.PostAsync(Run, null, ct);
        using var settings = await jonas.GetAsync(Settings, ct);
        using var admin = await freja.PostAsync(Run, null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.NotNull(await admin.Content.ReadFromJsonAsync<LifecycleRunResult>(ApiJson.Options, ct));
    }
}
