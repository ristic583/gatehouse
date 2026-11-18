namespace Gatehouse.Application.Platform.Dtos;

/// <summary>Counts only (ADR-009 §2), fields of <c>tenant_statistics</c>.</summary>
public sealed record TenantStatisticsDto(
    int People,
    int Employees,
    int TenantAdmins,
    int Sites,
    int Floors,
    int Areas,
    int Doors,
    int PacsInstances,
    int ConnectorsOnline,
    int ConnectorsOffline,
    int ConnectorsNeverSeen,
    int ActiveEntitlements,
    int PendingRequests,
    int OpenAlarms,
    int UnconfirmedFulfillments,
    int LinkedPeople,
    DateTimeOffset? OnboardingCompletedAt,
    DateTimeOffset ComputedAt);
