using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("cards");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DomainEvents);
        builder.Property(c => c.CardNumber).HasMaxLength(20);
        builder.Property(c => c.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Version).IsRowVersion();
        builder.HasOne<Person>().WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.CardNumber).IsUnique().HasFilter("state <> 'Revoked'");
        builder.HasIndex(c => c.PersonId).IsUnique().HasFilter("state <> 'Revoked'");
    }
}
