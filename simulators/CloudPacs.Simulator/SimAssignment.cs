namespace CloudPacs.Simulator;

public sealed record SimAssignment(
    Guid AssignmentId, string GroupId, string ExternalRef, string CardNumber, bool Active, DateTimeOffset AddedAt, DateTimeOffset? RemovedAt);
