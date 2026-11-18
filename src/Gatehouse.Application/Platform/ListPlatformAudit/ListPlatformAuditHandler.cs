using System.Text.Json;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.ListPlatformAudit;

public sealed class ListPlatformAuditHandler(IPlatformCatalogDb catalog)
{
    public const int MaxLimit = 200;
    public const int DefaultLimit = 50;

    public async Task<IReadOnlyList<PlatformAuditEntryDto>> HandleAsync(ListPlatformAuditQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entries = catalog.PlatformAuditEntries.AsNoTracking();
        if (!string.IsNullOrEmpty(query.TenantId))
        {
            var id = TenantId.Create(query.TenantId);
            if (id.IsFailure)
            {
                return [];
            }

            TenantId? tenantId = id.Value;
            entries = entries.Where(e => e.TenantId == tenantId);
        }

        if (query.Before is { } before)
        {
            entries = entries.Where(e => e.Sequence < before);
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await entries.OrderByDescending(e => e.Sequence).Take(limit).ToListAsync(ct);
        return rows.Select(e =>
        {
            using var doc = JsonDocument.Parse(e.Data);
            return new PlatformAuditEntryDto(
                e.Sequence, e.OccurredAt, e.ActorKind.ToString(), e.ActorId, e.Action, e.TenantId?.Value, doc.RootElement.Clone());
        }).ToList();
    }
}
