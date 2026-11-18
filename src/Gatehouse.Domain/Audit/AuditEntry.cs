using System.Text.RegularExpressions;

namespace Gatehouse.Domain.Audit;

public sealed partial class AuditEntry
{
    private static long s_localOrder;

    private AuditEntry()
    {
        ActorId = Action = SubjectType = SubjectId = Payload = PrevHash = Hash = string.Empty;
    }

    public long Sequence { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditActorKind ActorKind { get; private set; }

    public string ActorId { get; private set; }

    public string Action { get; private set; }

    public string SubjectType { get; private set; }

    public string SubjectId { get; private set; }

    public string Payload { get; private set; }

    public string PrevHash { get; private set; }

    public string Hash { get; private set; }

    /// <summary>Creation order within the process; not persisted.</summary>
    public long LocalOrder { get; private set; }

    public static AuditEntry CreatePending(
        AuditActor actor,
        string action,
        string subjectType,
        string subjectId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (!ActionPattern().IsMatch(action))
        {
            throw new ArgumentException($"Invalid audit action '{action}'.", nameof(action));
        }

        var utc = occurredAt.ToUniversalTime();
        var order = Interlocked.Increment(ref s_localOrder);
        return new AuditEntry
        {
            Sequence = -order, // unique temporary key so several pending entries can be tracked; Seal assigns the real one
            OccurredAt = new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % 10), TimeSpan.Zero),
            ActorKind = actor.Kind,
            ActorId = actor.Id,
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Payload = AuditCanonicalJson.Canonicalize(payloadJson),
            LocalOrder = order,
        };
    }

    public void Seal(long sequence, string prevHash)
    {
        if (Hash.Length != 0)
        {
            throw new InvalidOperationException("The audit entry is already sealed.");
        }

        Sequence = sequence;
        PrevHash = prevHash;
        Hash = ComputeHash();
    }

    public bool HasValidHash() => Hash.Length != 0 && Hash == ComputeHash();

    private string ComputeHash() =>
        AuditHasher.Compute(PrevHash, Sequence, OccurredAt, ActorKind, ActorId, Action, SubjectType, SubjectId, Payload);

    [GeneratedRegex(@"^[a-z][a-z_]*(\.[a-z_]+)+$")]
    private static partial Regex ActionPattern();
}
