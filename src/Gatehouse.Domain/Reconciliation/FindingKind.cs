namespace Gatehouse.Domain.Reconciliation;

public enum FindingKind
{
    MissingInPacs,
    ExtraInPacs,
    Mismatch,
    ExpiredStillPresent,
}
