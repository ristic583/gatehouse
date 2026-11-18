using System.Text.Json;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Audit.Dtos;

public static class AuditEntryDtoMapper
{
    public static async Task<IReadOnlyList<AuditEntryDto>> MapAsync(IReadOnlyList<AuditEntry> entries, ITenantDb db, CancellationToken ct)
    {
        var ids = entries
            .Where(e => e.ActorKind == AuditActorKind.Person && Guid.TryParse(e.ActorId, out _))
            .Select(e => new PersonId(Guid.Parse(e.ActorId)))
            .Distinct()
            .ToList();
        var names = ids.Count == 0
            ? []
            : (await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct))
                .ToDictionary(p => p.Id.Value.ToString(), p => p.DisplayName);

        return entries
            .Select(e =>
            {
                using var doc = JsonDocument.Parse(e.Payload);
                string? name = e.ActorKind == AuditActorKind.Person && names.TryGetValue(e.ActorId, out var n) ? n : null;
                return new AuditEntryDto(
                    e.Sequence, e.OccurredAt, e.ActorKind.ToString(), e.ActorId, name, e.Action, e.SubjectType, e.SubjectId,
                    doc.RootElement.Clone(), e.Hash);
            })
            .ToList();
    }
}
