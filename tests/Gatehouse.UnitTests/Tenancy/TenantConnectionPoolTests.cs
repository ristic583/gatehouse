using Gatehouse.Infrastructure.Tenancy;
using Npgsql;

namespace Gatehouse.UnitTests.Tenancy;

public sealed class TenantConnectionPoolTests
{
    [Fact]
    public void Apply_overrides_stored_pool_settings_with_bounded_values()
    {
        // Shape of the connection strings stored in the catalog before M11-00.
        const string stored = "Host=db;Port=5432;Username=gh;Password=secret;Database=tenant_nordlys;Maximum Pool Size=20";

        var applied = new NpgsqlConnectionStringBuilder(TenantConnectionPool.Apply(stored));

        Assert.Equal("tenant_nordlys", applied.Database);
        Assert.Equal("db", applied.Host);
        Assert.Equal(10, applied.MaxPoolSize);
        Assert.Equal(0, applied.MinPoolSize);
        Assert.Equal(15, applied.ConnectionIdleLifetime);
        Assert.Equal(5, applied.ConnectionPruningInterval);
        Assert.True(applied.Pooling);
    }
}
