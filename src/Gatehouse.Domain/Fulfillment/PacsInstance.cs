using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors.Events;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Fulfillment;

public sealed class PacsInstance : AggregateRoot<PacsInstanceId>
{
    public static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(90);

    private List<SiteId> _siteIds = [];

    private PacsInstance(PacsInstanceId id, string name, PacsVendor vendor)
        : base(id)
    {
        Name = name;
        Vendor = vendor;
    }

    // EF Core
    private PacsInstance()
        : base(default)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public PacsVendor Vendor { get; private set; }

    public IReadOnlyCollection<SiteId> SiteIds
    {
        get => _siteIds.AsReadOnly();
        private set => _siteIds = [.. value];
    }

    public FulfillmentMode FulfillmentMode { get; private set; } = FulfillmentMode.Manual;

    public string? CapabilitiesJson { get; private set; }

    public DateTimeOffset? LastHeartbeatAt { get; private set; }

    public string? AgentVersion { get; private set; }

    public DateTimeOffset? OfflineNotifiedAt { get; private set; }

    public static Result<PacsInstance> Create(string name, PacsVendor vendor, IReadOnlyCollection<SiteId> siteIds)
    {
        ArgumentNullException.ThrowIfNull(siteIds);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("pacs.invalid_name", "PACS instance name must be 1-100 characters.");
        }

        return new PacsInstance(PacsInstanceId.New(), name, vendor) { _siteIds = [.. siteIds.Distinct()] };
    }

    /// <summary>Admin edit (M10). <paramref name="hasActiveRegistration"/>: a vendor change is refused while a connector is
    /// registered.</summary>
    public Result Update(string name, PacsVendor vendor, IReadOnlyCollection<SiteId> siteIds, bool hasActiveRegistration)
    {
        ArgumentNullException.ThrowIfNull(siteIds);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("pacs.invalid_name", "PACS instance name must be 1-100 characters.");
        }

        if (vendor != Vendor && hasActiveRegistration)
        {
            return Error.Conflict("pacs.in_use", "The vendor cannot change while a connector is registered.");
        }

        Name = name;
        Vendor = vendor;
        _siteIds = [.. siteIds.Distinct()];
        return Result.Success();
    }

    public void SetFulfillmentMode(FulfillmentMode mode) => FulfillmentMode = mode;

    public bool RecordHeartbeat(DateTimeOffset at, string agentVersion, string? capabilitiesJson)
    {
        if (!IsOnline(at))
        {
            Raise(new ConnectorCameOnline(Id, LastHeartbeatAt, at));
        }

        var changed = capabilitiesJson is not null && !string.Equals(capabilitiesJson, CapabilitiesJson, StringComparison.Ordinal);
        if (capabilitiesJson is not null)
        {
            CapabilitiesJson = capabilitiesJson;
        }

        LastHeartbeatAt = at;
        AgentVersion = agentVersion;
        OfflineNotifiedAt = null;
        return changed;
    }

    /// <summary>Raises <see cref="ConnectorWentOffline"/> once when a connector-mode instance stops sending heartbeats.</summary>
    public bool MarkOfflineIfStale(DateTimeOffset now)
    {
        if (FulfillmentMode != FulfillmentMode.Connector || LastHeartbeatAt is not { } last || IsOnline(now)
            || OfflineNotifiedAt is not null)
        {
            return false;
        }

        OfflineNotifiedAt = now;
        Raise(new ConnectorWentOffline(Id, last, now));
        return true;
    }

    public bool IsOnline(DateTimeOffset now) => LastHeartbeatAt is { } last && now - last <= OnlineThreshold;
}
