using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.IntegrationTests.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Cli;

public sealed class PlatformBootstrapTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Bootstrap_creates_platform_operator()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);

        for (var run = 0; run < 2; run++)
        {
            await using var db = CatalogTestDb.Open(cs);
            var created = await new PlatformOperatorBootstrapper(db, TimeProvider.System).EnsureAsync(" Operator@Platform.test ", ct);
            Assert.Equal(run == 0, created);
        }

        await using var check = CatalogTestDb.Open(cs);
        var op = await check.PlatformOperators.SingleAsync(ct);
        Assert.Equal("operator@platform.test", op.Email);
        Assert.Equal(PlatformOperatorStatus.Active, op.Status);
        Assert.Null(op.Subject);
    }
}
