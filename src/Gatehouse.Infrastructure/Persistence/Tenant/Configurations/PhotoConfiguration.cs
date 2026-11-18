using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("photos");
        builder.HasKey(p => p.PersonId);
        builder.Property(p => p.ObjectKey).HasMaxLength(300).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(20).IsRequired();
        builder.HasOne<Person>().WithOne().HasForeignKey<Photo>(p => p.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}
