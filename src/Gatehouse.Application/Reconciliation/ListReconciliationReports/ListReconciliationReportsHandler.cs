using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.ListReconciliationReports;

/// <summary>Reports of the instances the caller may view, newest first (max 50).</summary>
public sealed class ListReconciliationReportsHandler(ITenantDb db, ReconciliationReadModel read)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public async Task<Result<IReadOnlyList<ReconciliationReportSummaryDto>>> HandleAsync(
        ListReconciliationReportsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var instances = await read.VisibleInstancesAsync(ct);
        if (query.PacsInstanceId is { } only)
        {
            instances = [.. instances.Where(i => i.Id.Value == only)];
        }

        var ids = instances.Select(i => i.Id).ToList();
        var reports = await db.ReconciliationReports.AsNoTracking()
            .Where(r => ids.Contains(r.PacsInstanceId))
            .OrderByDescending(r => r.RequestedAt)
            .Take(limit)
            .ToListAsync(ct);
        return await read.SummariesAsync(reports, instances.ToDictionary(i => i.Id, i => i.Name), ct);
    }
}
