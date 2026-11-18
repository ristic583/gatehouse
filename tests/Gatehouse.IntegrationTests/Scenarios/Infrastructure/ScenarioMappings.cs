using Gatehouse.Application.Fulfillment.PacsAdmin;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// Edits the shared demo access-level mappings as tenant admin (<c>PUT /admin/pacs-instances/{id}/mappings</c>). The returned handle
/// restores the original mapping list when disposed (the one allowed exception to "never restore shared data").
/// </summary>
public sealed class ScenarioMappings(ScenarioAppHostFixture fixture)
{
    public async Task<PacsInstanceAdminDto> InstanceAsync(string pacsInstanceName, CancellationToken ct)
    {
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var instances = await admin.GetAsync<List<PacsInstanceAdminDto>>("v1/admin/pacs-instances", ct);
        return instances.Single(i => i.Name == pacsInstanceName);
    }

    /// <summary>Points the area's mapping at another vendor level ref; dispose to restore.</summary>
    public Task<IAsyncDisposable> RemapAsync(ScenarioArea area, string vendorAccessLevelRef, CancellationToken ct) =>
        ChangeAsync(area, (areaId, mappings) => mappings
            .Select(m => m.AreaId == areaId ? m with { VendorAccessLevelRef = vendorAccessLevelRef, DisplayName = vendorAccessLevelRef } : m)
            .ToList(), ct);

    /// <summary>Removes the area's mapping; dispose to restore.</summary>
    public Task<IAsyncDisposable> DeleteAsync(ScenarioArea area, CancellationToken ct) =>
        ChangeAsync(area, (areaId, mappings) => mappings.Where(m => m.AreaId != areaId).ToList(), ct);

    private async Task<IAsyncDisposable> ChangeAsync(
        ScenarioArea area, Func<Guid, List<AccessLevelMappingRequest>, List<AccessLevelMappingRequest>> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var instance = await InstanceAsync(area.PacsInstanceName, ct);
        var areaId = await fixture.Access.AreaIdAsync(area, ct);
        var original = instance.Mappings.Select(m => new AccessLevelMappingRequest(m.AreaId, m.VendorAccessLevelRef, m.DisplayName)).ToList();
        if (original.All(m => m.AreaId != areaId))
        {
            throw new InvalidOperationException($"{area.Code} has no mapping on {area.PacsInstanceName}.");
        }

        await PutAsync(admin, instance.Id, change(areaId, original), ct);
        return new Restore(admin, instance.Id, original);
    }

    private static async Task PutAsync(ScenarioClient admin, Guid instanceId, List<AccessLevelMappingRequest> mappings, CancellationToken ct)
    {
        using var response = await admin.PutAsync($"v1/admin/pacs-instances/{instanceId}/mappings", mappings, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"PUT mappings: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }
    }

    private sealed class Restore(ScenarioClient admin, Guid instanceId, List<AccessLevelMappingRequest> original) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await PutAsync(admin, instanceId, original, CancellationToken.None);
    }
}
