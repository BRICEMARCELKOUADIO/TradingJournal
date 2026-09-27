using System.ComponentModel.DataAnnotations.Schema;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Domain.Entities;

public sealed class Trade
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public MarketType MarketType { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public Direction Direction { get; set; }
    public decimal PositionSize { get; set; }
    public DateTime EntryDateTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public DateTime? ExitDateTime { get; private set; }
    public decimal? ExitPrice { get; private set; }
    public decimal? ResultAmount { get; private set; }
    public Status Status { get; private set; } = Status.Open;
    public Guid? StrategyId { get; set; }
    public Guid? EntryEmotionId { get; set; }
    public Guid? ExitEmotionId { get; set; }
    public string? EntryReason { get; set; }
    public string? ExitReason { get; set; }
    public bool? PlanRespected { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    public User User { get; set; } = null!;
    public Strategy? Strategy { get; set; }
    [ForeignKey(nameof(EntryEmotionId))]
    public Emotion? EntryEmotion { get; set; }
    [ForeignKey(nameof(ExitEmotionId))]
    public Emotion? ExitEmotion { get; set; }
    public ICollection<TradeImage> Images { get; set; } = new List<TradeImage>();

    public void MarkUpdated() => UpdatedAt = DateTime.UtcNow;

    // Validate all inputs before changing state, so a failed close leaves the trade intact.
    public void Close(DateTime? exitDateTime, decimal? exitPrice, decimal? resultAmount)
    {
        if (Status == Status.Closed)
            throw new InvalidOperationException("The trade is already closed.");
        ArgumentNullException.ThrowIfNull(exitDateTime);
        ArgumentNullException.ThrowIfNull(exitPrice);
        ArgumentNullException.ThrowIfNull(resultAmount);

        ExitDateTime = exitDateTime;
        ExitPrice = exitPrice;
        ResultAmount = resultAmount;
        Status = Status.Closed;
        UpdatedAt = DateTime.UtcNow;
    }
}
