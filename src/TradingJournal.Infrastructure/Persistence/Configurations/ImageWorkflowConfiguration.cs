using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Infrastructure.Images;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class ImageUploadConfiguration : IEntityTypeConfiguration<ImageUpload>
{
    public void Configure(EntityTypeBuilder<ImageUpload> b)
    {
        b.ToTable("ImageUploads");
        b.HasKey(x => x.Id);
        b.Property(x => x.StagingKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.FinalKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2");
        b.HasIndex(x => new { x.UserId, x.TradeId, x.ExpiresAtUtc });
        b.HasIndex(x => x.ExpiresAtUtc);
    }
}

public sealed class ImageDeletionConfiguration : IEntityTypeConfiguration<ImageDeletion>
{
    public void Configure(EntityTypeBuilder<ImageDeletion> b)
    {
        b.ToTable("ImageDeletions");
        b.HasKey(x => x.Id);
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.NextAttemptAtUtc).HasColumnType("datetime2");
        b.HasIndex(x => x.NextAttemptAtUtc);
    }
}
