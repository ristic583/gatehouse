using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Infrastructure.Tenancy;

public sealed class TenantScopeFactory(IServiceScopeFactory scopeFactory) : ITenantScopeFactory
{
    public async Task<AsyncServiceScope> CreateScopeAsync(TenantId tenantId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>();
            var connection = await resolver.ResolveAsync(tenantId, ct);
            if (connection.IsFailure)
            {
                throw new TenantNotResolvedException($"Tenant '{tenantId}' cannot be used: {connection.Error.Code}.");
            }

            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId, connection.Value);
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}
