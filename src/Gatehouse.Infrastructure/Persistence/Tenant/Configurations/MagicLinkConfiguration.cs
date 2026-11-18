using Gatehouse.Domain.Identity;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class MagicLinkConfiguration : IEntityTypeConfiguration<MagicLink>
{
    public void Configure(EntityTypeBuilder<MagicLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("magic_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(l => l.TokenHash).IsUnique();
        builder.HasIndex(l => new { l.PersonId, l.CreatedAt }).IsDescending(false, true);
        builder.HasOne<Person>().WithMany().HasForeignKey(l => l.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Person>().WithMany().HasForeignKey(l => l.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}
