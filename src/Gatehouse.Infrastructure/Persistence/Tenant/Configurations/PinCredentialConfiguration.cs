using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class PinCredentialConfiguration : IEntityTypeConfiguration<PinCredential>
{
    public void Configure(EntityTypeBuilder<PinCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("pin_credentials");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("person_id");
        builder.Ignore(p => p.PersonId);
        builder.Ignore(p => p.DomainEvents);
        builder.Property(p => p.Ciphertext).IsRequired();
        builder.Property(p => p.KeyId).HasMaxLength(50);
        builder.HasOne<Person>().WithOne().HasForeignKey<PinCredential>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
    }
}
