using Gatehouse.Domain.Connectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class ConnectorAuthorityConfiguration : IEntityTypeConfiguration<ConnectorAuthority>
{
    public void Configure(EntityTypeBuilder<ConnectorAuthority> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("connector_authorities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
    }
}
