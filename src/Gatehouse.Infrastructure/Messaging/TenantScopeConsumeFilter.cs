using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Tenancy;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>Binds the consume scope to the tenant named by the <c>gh-tenant-id</c> header (messages.md §0).</summary>
public sealed class TenantScopeConsumeFilter<T>(ICatalogReader catalog, ITenantConnectionResolver resolver, ITenantContextSetter setter)
    : IFilter<ConsumeContext<T>>
    where T : class
{
    public const string HeaderName = "gh-tenant-id";

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var header = context.Headers.Get<string>(HeaderName);
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new MissingTenantHeaderException();
        }

        var id = TenantId.Create(header);
        if (id.IsFailure || await catalog.FindAsync(id.Value, context.CancellationToken) is null)
        {
            throw new MissingTenantHeaderException($"Tenant '{header}' is not known.");
        }

        var connection = await resolver.ResolveAsync(id.Value, context.CancellationToken);
        if (connection.IsFailure)
        {
            throw new TenantNotResolvedException($"Tenant '{header}' cannot be used: {connection.Error.Code}.");
        }

        setter.Set(id.Value, connection.Value);
        await next.Send(context);
    }

    public void Probe(ProbeContext context) => ArgumentNullException.ThrowIfNull(context);
}
