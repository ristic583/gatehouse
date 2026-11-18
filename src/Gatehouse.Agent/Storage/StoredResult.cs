namespace Gatehouse.Agent.Storage;

public sealed record StoredResult(string Status, string? PacsRef, string? ErrorJson, DateTimeOffset ProcessedAt);
