namespace Building.Simulator;

/// <summary>
/// Picks the controller for a door: doors listed by the CloudPACS simulator (cached <c>GET /dev/doors</c>, refreshed every 60 s)
/// go to CloudPACS, everything else to LegacyPACS. Without a CloudPACS client every door goes to LegacyPACS.
/// </summary>
public sealed class DoorControllerRouter(LegacyPacsControllerClient legacy, CloudPacsControllerClient? cloud, TimeProvider timeProvider, ILogger<DoorControllerRouter> logger)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private volatile DoorCache? _cache;

    public LegacyPacsControllerClient Legacy => legacy;

    public async Task<IDoorControllerClient> ForAsync(string doorRef, CancellationToken ct)
    {
        if (cloud is null)
        {
            return legacy;
        }

        var cloudDoors = await CloudDoorsAsync(ct);
        return cloudDoors.Contains(doorRef) ? cloud : legacy;
    }

    /// <summary>Union of both controllers' doors.</summary>
    public async Task<IReadOnlyList<ControllerDoor>> ListDoorsAsync(CancellationToken ct)
    {
        var doors = new List<ControllerDoor>(await legacy.ListDoorsAsync(ct));
        if (cloud is not null)
        {
            doors.AddRange(await cloud.ListDoorsAsync(ct));
        }

        return doors;
    }

    private async Task<HashSet<string>> CloudDoorsAsync(CancellationToken ct)
    {
        var cache = _cache;
        var now = timeProvider.GetUtcNow();
        if (cache is not null && now - cache.RefreshedAt < RefreshInterval)
        {
            return cache.Doors;
        }

        try
        {
            var doors = await cloud!.ListDoorsAsync(ct);
            var fresh = new DoorCache(new HashSet<string>(doors.Select(d => d.DoorRef), StringComparer.Ordinal), now);
            _cache = fresh;
            return fresh.Doors;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Listing CloudPACS doors failed; keeping {Count} cached doors", cache?.Doors.Count ?? 0);
            return cache?.Doors ?? [];
        }
    }

    private sealed record DoorCache(HashSet<string> Doors, DateTimeOffset RefreshedAt);
}
