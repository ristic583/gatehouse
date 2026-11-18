using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    private TenantId? _tenantId;
    private string? _connectionString;

    public TenantId TenantId => _tenantId ?? throw new TenantNotResolvedException();

    public bool IsResolved => _tenantId is not null;

    public string ConnectionString => _connectionString ?? throw new TenantNotResolvedException();

    public void Set(TenantId id, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_tenantId is { } current && current != id)
        {
            throw new InvalidOperationException("The tenant of a scope cannot be changed once it is set.");
        }

        _tenantId = id;
        _connectionString = TenantConnectionPool.Apply(id, connectionString);
    }
}
