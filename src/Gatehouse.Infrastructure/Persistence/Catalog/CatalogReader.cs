using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Persistence.Catalog;

public sealed class CatalogReader(CatalogDbContext db) : ICatalogReader
{
    public async Task<IReadOnlyList<TenantSummary>> GetActiveTenantsAsync(CancellationToken ct) =>
        await db.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .OrderBy(t => t.Id)
            .Select(t => new TenantSummary(t.Id, t.DisplayName, t.Status, t.IdpKind))
            .ToListAsync(ct);

    public Task<TenantSummary?> FindAsync(TenantId id, CancellationToken ct) =>
        db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantSummary(t.Id, t.DisplayName, t.Status, t.IdpKind))
            .SingleOrDefaultAsync(ct);
}
