using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gatehouse.Infrastructure.Persistence.Catalog;

public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=design;Username=x;Password=x")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new CatalogDbContext(options);
    }
}
