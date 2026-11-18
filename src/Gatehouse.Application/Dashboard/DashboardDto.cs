namespace Gatehouse.Application.Dashboard;

/// <summary>Business dashboard (M9-11): only counts the app already tracks; every number links to the console view listing the
/// items.</summary>
public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    int PendingApprovals,
    int OpenAlarms,
    IReadOnlyDictionary<string, int> OpenTasksByKind,
    int OpenFindingTasks,
    int OpenDriftAlerts,
    int UnconfirmedFulfillmentsOverOneHour,
    IReadOnlyList<DashboardCoverageDto> Reconciliation,
    DashboardConnectorsDto Connectors);

/// <summary>Last completed reconciliation of a connector-mode instance (null fields when none ran yet).</summary>
public sealed record DashboardCoverageDto(
    Guid PacsInstanceId, string Name, DateTimeOffset? ComparedAt, int? ComparedRecords, int? InSync, int? Findings);

public sealed record DashboardConnectorsDto(int Online, int Offline, int NeverSeen);
