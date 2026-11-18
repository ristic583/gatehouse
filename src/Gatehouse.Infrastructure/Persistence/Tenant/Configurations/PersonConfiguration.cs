using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("persons");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.DisplayName);
        builder.Property(p => p.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.GivenName).HasMaxLength(100);
        builder.Property(p => p.FamilyName).HasMaxLength(100);
        builder.Property(p => p.Email).HasMaxLength(254);
        builder.Property(p => p.IdentitySubject).HasMaxLength(255);
        builder.Property(p => p.Version).IsRowVersion();
        builder.ComplexProperty(p => p.Eligibility, e =>
        {
            e.Property(x => x.Start).HasColumnName("eligibility_start");
            e.Property(x => x.End).HasColumnName("eligibility_end");
            e.Property(x => x.TimeZoneId).HasColumnName("eligibility_time_zone_id").HasMaxLength(64);
        });
        builder.HasOne<Person>().WithMany().HasForeignKey(p => p.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(p => p.SponsorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.Email).IsUnique();
        builder.HasIndex(p => p.IdentitySubject).IsUnique().HasFilter("identity_subject IS NOT NULL");
    }
}
