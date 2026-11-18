using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ReaderConfiguration : IEntityTypeConfiguration<Reader>
{
    public void Configure(EntityTypeBuilder<Reader> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("readers");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.PacsReaderRef).HasMaxLength(100);
        builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(10);
        builder.ComplexProperty(r => r.Position, p =>
        {
            p.Property(x => x.X).HasColumnName("position_x");
            p.Property(x => x.Y).HasColumnName("position_y");
        });
        builder.HasOne<Door>().WithMany().HasForeignKey(r => r.DoorId).OnDelete(DeleteBehavior.Cascade);
    }
}
