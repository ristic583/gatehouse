using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed class GetIdentityProviderHandler(IdentityProviderStore store, IdentityProviderViews views)
{
    public async Task<Result<IdentityProviderDto?>> HandleAsync(CancellationToken ct) =>
        await views.ToDtoAsync(await store.GetAsync(ct), ct);
}
