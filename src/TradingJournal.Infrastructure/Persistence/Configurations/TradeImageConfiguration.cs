using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class TradeImageConfiguration : IEntityTypeConfiguration<TradeImage>
{
    public void Configure(EntityTypeBuilder<TradeImage> builder)
    {
        builder.ToTable("TradeImages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier");
        builder.Property(x => x.TradeId).HasColumnType("uniqueidentifier").IsRequired();
        builder.Property(x => x.StorageKey).IsUnicode().HasMaxLength(500).IsRequired();
        builder.Property(x => x.FileName).IsUnicode().HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).IsUnicode().HasMaxLength(100).IsRequired();
        builder.Property(x => x.ImageType).HasConversion<int>();
        builder.Property(x => x.DisplayOrder).HasColumnType("int");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2");
        builder.HasIndex(x => x.TradeId);
        // Deletes metadata only. Physical storage cleanup belongs to the future storage service.
        builder.HasOne(x => x.Trade).WithMany(x => x.Images)
            .HasForeignKey(x => x.TradeId).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}
