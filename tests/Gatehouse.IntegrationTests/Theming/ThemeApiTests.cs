using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Theming;
using Gatehouse.Domain.Theming;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Theming;

/// <summary>Theme API (M9-02); the nordlys theme row is removed afterwards.</summary>
public sealed class ThemeApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Theme = new("/api/v1/theme", UriKind.Relative);
    private static readonly Uri AdminTheme = new("/api/v1/admin/theme", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Member_reads_default_theme()
    {
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        var theme = await anna.GetFromJsonAsync<ThemeDto>(Theme, ApiJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(TenantTheme.Default.Primary, theme!.Primary);
        Assert.Empty(theme.Terms);
    }

    [Fact]
    public async Task Admin_saves_theme_and_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var request = new ThemeDto("#1e3a8a", "#ffffff", "#eff6ff", "#1e3a8a", "#ffffff", "#0f172a", 6, new Dictionary<string, string> { ["Area"] = "Zone" });

        using var response = await freja.PutAsJsonAsync(AdminTheme, request, ApiJson.Options, ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var read = await anna.GetFromJsonAsync<ThemeDto>(Theme, ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("#1e3a8a", read!.Primary);
        Assert.Equal("Zone", read.Terms["Area"]);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var entry = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.Action == "theme.changed").OrderByDescending(a => a.Sequence).FirstAsync(ct);
        Assert.Contains("primary", entry.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Low_contrast_is_refused_with_pairs()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var request = new ThemeDto("#ffff00", "#ffffff", "#eff6ff", "#1e3a8a", "#ffffff", "#0f172a", 6, new Dictionary<string, string>());

        using var response = await freja.PutAsJsonAsync(AdminTheme, request, ApiJson.Options, ct);
        using var invalid = await freja.PutAsJsonAsync(AdminTheme, request with { Primary = "yellow" }, ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("theme.contrast_failed", body, StringComparison.Ordinal);
        Assert.Contains("\"primary\"", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("theme.invalid", await invalid.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_admin_cannot_save_theme()
    {
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await anna.PutAsJsonAsync(AdminTheme, ThemeStore.ToDto(TenantTheme.Default), ApiJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
