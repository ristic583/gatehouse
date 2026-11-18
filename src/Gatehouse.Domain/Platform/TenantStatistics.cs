using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Platform;

public sealed class TenantStatistics
{
    public TenantId TenantId { get; set; }

    public int People { get; set; }

    public int Employees { get; set; }

    public int TenantAdmins { get; set; }

    public int Sites { get; set; }

    public int Floors { get; set; }

    public int Areas { get; set; }

    public int Doors { get; set; }

    public int PacsInstances { get; set; }

    public int ConnectorsOnline { get; set; }

    public int ConnectorsOffline { get; set; }

    public int ConnectorsNeverSeen { get; set; }

    public int ActiveEntitlements { get; set; }

    public int PendingRequests { get; set; }

    public int OpenAlarms { get; set; }

    public int UnconfirmedFulfillments { get; set; }

    public int LinkedPeople { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
