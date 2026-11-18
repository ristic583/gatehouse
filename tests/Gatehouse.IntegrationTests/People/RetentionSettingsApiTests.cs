using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.People.Retention;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>Retention settings routes (M9-06); the changed nordlys row is removed afterwards.</summary>
public sealed class RetentionSettingsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Settings = new("/api/v1/admin/retention/settings", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Settings_change_is_validated_and_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        var before = await freja.GetFromJsonAsync<RetentionSettingsDto>(Settings, ApiJson.Options, ct);
        using var changed = await freja.PutAsJsonAsync(Settings, new { key = RetentionSettings.PhotoDaysKey, days = 14 }, ct);
        using var tooShort = await freja.PutAsJsonAsync(Settings, new { key = RetentionSettings.PersonalDataDaysKey, days = 5 }, ct);
        using var run = await freja.PostAsync(new Uri("/api/v1/admin/retention/run", UriKind.Relative), null, ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var forbidden = await anna.GetAsync(Settings, ct);

        Assert.Equal(RetentionSettings.Defaults.PhotoDaysAfterTermination, before!.PhotoDaysAfterTermination);
        Assert.Equal(14, (await changed.Content.ReadFromJsonAsync<RetentionSettingsDto>(ApiJson.Options, ct))!.PhotoDaysAfterTermination);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("retention.invalid_setting", await tooShort.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        Assert.True(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "retention.settings_changed", ct));
    }
}
