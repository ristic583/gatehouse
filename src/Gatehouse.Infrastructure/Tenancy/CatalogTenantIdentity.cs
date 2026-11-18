using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Tenancy;

public sealed class CatalogTenantIdentity(CatalogDbContext catalog) : ICatalogTenantIdentity
{
    public Task<string?> GetEmailDomainAsync(TenantId tenantId, CancellationToken ct) =>
        catalog.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.EmailDomain).SingleOrDefaultAsync(ct);

    public async Task SetIdpKindAsync(TenantId tenantId, IdpKind kind, CancellationToken ct)
    {
        var tenant = await catalog.Tenants.SingleAsync(t => t.Id == tenantId, ct);
        tenant.SetIdpKind(kind);
        await catalog.SaveChangesAsync(ct);
    }
}
