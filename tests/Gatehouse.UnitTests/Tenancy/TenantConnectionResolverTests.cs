using Gatehouse.Infrastructure.Tenancy;

namespace Gatehouse.UnitTests.Tenancy;

public sealed class TenantConnectionResolverTests
{
    /// <summary>GH-AUTH-03: suspension evicts only the local cache, so the cache lifetime is the cross-process propagation delay.</summary>
    [Fact]
    public void Suspension_propagates_to_other_processes_within_ten_seconds() =>
        Assert.True(TenantConnectionResolver.CacheDuration <= TimeSpan.FromSeconds(10));
}
