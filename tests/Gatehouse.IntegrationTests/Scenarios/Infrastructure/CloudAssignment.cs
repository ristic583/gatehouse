namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A row of CloudPACS <c>GET /dev/assignments</c>.</summary>
public sealed record CloudAssignment(
    string AssignmentId,
    string GroupId,
    string ExternalRef,
    string? CardNumber,
    bool Active,
    DateTimeOffset AddedAt,
    DateTimeOffset? RemovedAt,
    string? HolderName);
