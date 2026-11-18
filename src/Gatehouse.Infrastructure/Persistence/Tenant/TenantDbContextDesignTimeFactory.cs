using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gatehouse.Infrastructure.Persistence.Tenant;

public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Database=design;Username=x;Password=x")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new TenantDbContext(options);
    }
}
