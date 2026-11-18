namespace Gatehouse.Adapters.CloudPacs;

/// <summary>One page of a CloudPACS list endpoint (<c>{items, nextCursor}</c>).</summary>
internal sealed record CloudPacsPage<T>(IReadOnlyList<T>? Items, string? NextCursor);

internal sealed record CloudPacsCardholderItem(Guid Id, string ExternalRef);

internal sealed record CloudPacsCardItem(string CardNumber, string ExternalRef, string State);

internal sealed record CloudPacsAssignmentItem(Guid AssignmentId, string GroupId, string ExternalRef, string? CardNumber, bool Active);

/// <summary>One vendor event in a webhook payload (JSON array).</summary>
internal sealed record CloudPacsWebhookEvent(
    Guid Id, string Type, string DoorRef, string? ReaderRef, string? CardNumber, string? ExternalRef, string? Direction,
    DateTimeOffset OccurredAt);
