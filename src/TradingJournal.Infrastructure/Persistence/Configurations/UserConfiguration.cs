using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier");
        builder.Property(x => x.DisplayName).IsUnicode().HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).IsUnicode().HasMaxLength(255).IsRequired();
        builder.Property(x => x.PasswordHash).IsUnicode().HasMaxLength(500).IsRequired();
        builder.Property(x => x.EmailVerified).HasColumnType("bit");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2");
        builder.Property(x => x.LastLoginAt).HasColumnType("datetime2");
        builder.HasIndex(x => x.Email).IsUnique();
    }
}
