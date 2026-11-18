using System.Text.Json;

namespace Gatehouse.Application.Platform.Dtos;

public sealed record PlatformAuditEntryDto(
    long Sequence,
    DateTimeOffset OccurredAt,
    string ActorKind,
    string ActorId,
    string Action,
    string? TenantId,
    JsonElement Data);
