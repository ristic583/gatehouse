using System.Text.Json;

namespace Gatehouse.Application.Audit.Dtos;

public sealed record AuditEntryDto(
    long Sequence,
    DateTimeOffset OccurredAt,
    string ActorKind,
    string ActorId,
    string? ActorName,
    string Action,
    string SubjectType,
    string SubjectId,
    JsonElement Payload,
    string Hash);
