using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Infrastructure.Persistence.Configurations;

public sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades", table => table.HasCheckConstraint("CK_Trades_ClosedRequiresExit",
            $"[Status] IN ({(int)Status.Open}, {(int)Status.Closed}) AND ([Status] <> {(int)Status.Closed} OR " +
            "([ExitDateTime] IS NOT NULL AND [ExitPrice] IS NOT NULL AND [ResultAmount] IS NOT NULL))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnType("uniqueidentifier");
        builder.Property(x => x.UserId).HasColumnType("uniqueidentifier").IsRequired();
        builder.Property(x => x.StrategyId).HasColumnType("uniqueidentifier");
        builder.Property(x => x.EntryEmotionId).HasColumnType("uniqueidentifier");
        builder.Property(x => x.ExitEmotionId).HasColumnType("uniqueidentifier");
        builder.Property(x => x.Symbol).IsUnicode().HasMaxLength(30).IsRequired();
        builder.Property(x => x.PositionSize).HasPrecision(18, 6);
        builder.Property(x => x.EntryPrice).HasPrecision(18, 8);
        builder.Property(x => x.ExitPrice).HasPrecision(18, 8);
        builder.Property(x => x.StopLoss).HasPrecision(18, 8);
        builder.Property(x => x.TakeProfit).HasPrecision(18, 8);
        builder.Property(x => x.ResultAmount).HasPrecision(18, 2);
        builder.Property(x => x.EntryReason).IsUnicode().HasMaxLength(1000);
        builder.Property(x => x.ExitReason).IsUnicode().HasMaxLength(1000);
        builder.Property(x => x.Notes).IsUnicode().HasMaxLength(2000);
        builder.Property(x => x.EntryDateTime).HasColumnType("datetime2");
        builder.Property(x => x.ExitDateTime).HasColumnType("datetime2");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2");
        builder.Property(x => x.MarketType).HasConversion<int>();
        builder.Property(x => x.Direction).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PlanRespected).HasColumnType("bit");
        builder.HasIndex(x => new { x.UserId, x.EntryDateTime });
        builder.HasIndex(x => new { x.UserId, x.MarketType });

        // SQL NO ACTION, with no implicit client-side deletion or FK nulling either.
        builder.HasOne(x => x.User).WithMany(x => x.Trades)
            .HasForeignKey(x => x.UserId).IsRequired().OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne(x => x.Strategy).WithMany(x => x.Trades)
            .HasForeignKey(x => x.StrategyId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne(x => x.EntryEmotion).WithMany()
            .HasForeignKey(x => x.EntryEmotionId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne(x => x.ExitEmotion).WithMany()
            .HasForeignKey(x => x.ExitEmotionId).IsRequired(false).OnDelete(DeleteBehavior.ClientNoAction);
    }
}
