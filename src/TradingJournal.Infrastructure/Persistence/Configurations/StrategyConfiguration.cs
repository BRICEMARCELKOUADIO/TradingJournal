using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.ToTable("Strategies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier");
        builder.Property(x => x.UserId).HasColumnType("uniqueidentifier");
        builder.Property(x => x.Name).IsUnicode().HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).IsUnicode().HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2");
        builder.HasOne(x => x.User).WithMany(x => x.Strategies)
            .HasForeignKey(x => x.UserId).IsRequired(false)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
