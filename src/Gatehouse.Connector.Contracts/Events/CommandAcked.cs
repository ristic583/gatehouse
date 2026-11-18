namespace Gatehouse.Connector.Contracts.Events;

public sealed record CommandAcked(
    Guid CommandId, string IdempotencyKey, AckStatus Status, string? PacsRef, DateTimeOffset? AppliedAt, AckError? Error);
