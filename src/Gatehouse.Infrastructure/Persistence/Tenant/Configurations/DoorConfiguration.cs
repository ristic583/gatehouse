using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class DoorConfiguration : IEntityTypeConfiguration<Door>
{
    public void Configure(EntityTypeBuilder<Door> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("doors");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100);
        builder.Property(d => d.PacsDoorRef).HasMaxLength(100);
        builder.ComplexProperty(d => d.Position, p =>
        {
            p.Property(x => x.X).HasColumnName("position_x");
            p.Property(x => x.Y).HasColumnName("position_y");
        });
        builder.HasOne<Area>().WithMany().HasForeignKey(d => d.AreaId).OnDelete(DeleteBehavior.Cascade);
    }
}
