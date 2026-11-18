namespace Gatehouse.Connector.Contracts.Commands;

/// <summary>
/// <paramref name="ExecuteBy"/>: absolute deadline; gateway and agent drop the command after it (GH-LIVE-01). A missing deadline (rows
/// from before GH-LIVE-01, a producer that forgets it) fails closed: <see cref="DeadlineOrDefault"/> uses the issue time +
/// <see cref="DefaultDeadline"/> (PT-33).
/// </summary>
public sealed record MomentaryUnlock(string[] PacsDoorRefs, int DurationSeconds, string Reason, DateTimeOffset? ExecuteBy = null)
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(60);

    public DateTimeOffset DeadlineOrDefault(DateTimeOffset issuedAt) => ExecuteBy ?? issuedAt + DefaultDeadline;
}
