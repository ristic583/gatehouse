using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Platform;

public sealed class PlatformAuditEntry
{
    private PlatformAuditEntry()
    {
        ActorId = string.Empty;
        Action = string.Empty;
        Data = "{}";
        PreviousHash = string.Empty;
        Hash = string.Empty;
    }

    public Guid Id { get; private set; }

    public long Sequence { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public PlatformActorKind ActorKind { get; private set; }

    public string ActorId { get; private set; }

    public string Action { get; private set; }

    public TenantId? TenantId { get; private set; }

    public string Data { get; private set; }

    public string PreviousHash { get; private set; }

    public string Hash { get; private set; }

    public static PlatformAuditEntry Create(
        long sequence,
        DateTimeOffset occurredAt,
        PlatformActor actor,
        string action,
        TenantId? tenantId,
        string dataJson,
        string previousHash,
        string hash) => new()
        {
            Id = Guid.CreateVersion7(),
            Sequence = sequence,
            OccurredAt = occurredAt,
            ActorKind = actor.Kind,
            ActorId = actor.Id,
            Action = action,
            TenantId = tenantId,
            Data = dataJson,
            PreviousHash = previousHash,
            Hash = hash,
        };
}
