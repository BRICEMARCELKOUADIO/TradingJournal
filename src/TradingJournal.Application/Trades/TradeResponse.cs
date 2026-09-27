using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Application.Trades;

public sealed record TradeResponse(
    Guid Id, MarketType MarketType, string Symbol, Direction Direction, decimal PositionSize,
    DateTime EntryDateTime, decimal EntryPrice, decimal? StopLoss, decimal? TakeProfit,
    DateTime? ExitDateTime, decimal? ExitPrice, decimal? ResultAmount, Status Status,
    Guid? StrategyId, Guid? EntryEmotionId, Guid? ExitEmotionId, string? EntryReason,
    string? ExitReason, bool? PlanRespected, string? Notes, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static TradeResponse FromEntity(Trade trade) => new(
        trade.Id, trade.MarketType, trade.Symbol, trade.Direction, trade.PositionSize,
        Utc(trade.EntryDateTime), trade.EntryPrice, trade.StopLoss, trade.TakeProfit,
        Utc(trade.ExitDateTime), trade.ExitPrice, trade.ResultAmount, trade.Status,
        trade.StrategyId, trade.EntryEmotionId, trade.ExitEmotionId, trade.EntryReason,
        trade.ExitReason, trade.PlanRespected, trade.Notes, Utc(trade.CreatedAt), Utc(trade.UpdatedAt));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
}

public sealed record TradePage(IReadOnlyList<TradeResponse> Items, int Page, int PageSize, int TotalCount);
