namespace Gatehouse.Connector.Contracts.Events;

/// <summary>A local scheduled revocation the PACS refused permanently and that is still pending (GH-CONN-03).</summary>
public sealed record RefusedRemoval(string RevocationRef, string Code, DateTimeOffset FirstRefusedAt, int Attempts);
