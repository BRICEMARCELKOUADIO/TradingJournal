using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Domain;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class EmotionConfiguration : IEntityTypeConfiguration<Emotion>
{
    public void Configure(EntityTypeBuilder<Emotion> builder)
    {
        builder.ToTable("Emotions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier");
        builder.Property(x => x.Name).IsUnicode().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).IsUnicode().HasMaxLength(250);
        builder.HasData(PredefinedEmotions.Create());
    }
}
