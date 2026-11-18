namespace CloudPacs.Simulator;

public sealed record SimEvent(
    Guid Id, long Sequence, string Type, string DoorRef, string? ReaderRef, string? CardNumber, string? ExternalRef, string? Direction, DateTimeOffset OccurredAt);
