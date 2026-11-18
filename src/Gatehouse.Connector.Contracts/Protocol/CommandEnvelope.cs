using System.Text.Json;

namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record CommandEnvelope(
    Guid CommandId,
    string IdempotencyKey,
    string Type,
    int SchemaVersion,
    string TenantId,
    Guid PacsInstanceId,
    string PartitionKey,
    long Sequence,
    string Priority,
    DateTimeOffset IssuedAt,
    DateTimeOffset LeaseUntil,
    JsonElement Payload,
    string? TraceParent = null);
