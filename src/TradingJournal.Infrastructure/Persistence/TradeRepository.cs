using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Application.Trades;
using TradingJournal.Domain.Entities;
using TradingJournal.Infrastructure.Images;

namespace TradingJournal.Infrastructure.Persistence;

public sealed class TradeRepository(TradingJournalDbContext context) : ITradeRepository
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) => context.Users.AnyAsync(u => u.Id == userId, ct);
    public Task<bool> StrategyAccessibleAsync(Guid userId, Guid strategyId, CancellationToken ct) =>
        context.Strategies.AnyAsync(s => s.Id == strategyId && (s.UserId == null || s.UserId == userId), ct);
    public Task<bool> EmotionExistsAsync(Guid emotionId, CancellationToken ct) => context.Emotions.AnyAsync(e => e.Id == emotionId, ct);

    public async Task AddAsync(Trade trade, CancellationToken ct)
    {
        context.Trades.Add(trade);
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 547 })
        {
            throw new ValidationException("Une référence du trade n'est plus disponible.");
        }
    }

    public async Task<TradeResponse?> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var trade = await Owned(userId).AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        return trade is null ? null : TradeResponse.FromEntity(trade);
    }

    public async Task<TradePage> ListAsync(Guid userId, TradeQuery query, CancellationToken ct)
    {
        var trades = Owned(userId).AsNoTracking();
        if (query.StartDate.HasValue) trades = trades.Where(t => t.EntryDateTime >= query.StartDate.Value.UtcDateTime);
        if (query.EndDate.HasValue) trades = trades.Where(t => t.EntryDateTime <= query.EndDate.Value.UtcDateTime);
        if (query.MarketType.HasValue) trades = trades.Where(t => t.MarketType == query.MarketType);
        if (query.Direction.HasValue) trades = trades.Where(t => t.Direction == query.Direction);
        if (query.Status.HasValue) trades = trades.Where(t => t.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Symbol))
        {
            var symbol = query.Symbol.Trim().ToUpperInvariant();
            trades = trades.Where(t => t.Symbol == symbol);
        }
        var count = await trades.CountAsync(ct);
        var items = await trades.OrderByDescending(t => t.EntryDateTime).ThenByDescending(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new TradePage(items.Select(TradeResponse.FromEntity).ToArray(), query.Page, query.PageSize, count);
    }

    public async Task<TradeResponse?> UpdateAsync(Guid userId, Guid id, Func<Trade, Task> update, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        // Serialize close/PUT for this owned row without changing the existing schema.
        var trade = await context.Trades.FromSqlInterpolated(
            $"SELECT * FROM [Trades] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id} AND [UserId] = {userId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (trade is null) return null;
        await update(trade);
        try
        {
            // Keep the owner predicate in the UPDATE itself, not only in the preceding SELECT.
            await Owned(userId).Where(t => t.Id == id).ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.MarketType, trade.MarketType).SetProperty(t => t.Symbol, trade.Symbol)
                .SetProperty(t => t.Direction, trade.Direction).SetProperty(t => t.PositionSize, trade.PositionSize)
                .SetProperty(t => t.EntryDateTime, trade.EntryDateTime).SetProperty(t => t.EntryPrice, trade.EntryPrice)
                .SetProperty(t => t.StopLoss, trade.StopLoss).SetProperty(t => t.TakeProfit, trade.TakeProfit)
                .SetProperty(t => t.ExitDateTime, trade.ExitDateTime).SetProperty(t => t.ExitPrice, trade.ExitPrice)
                .SetProperty(t => t.ResultAmount, trade.ResultAmount).SetProperty(t => t.Status, trade.Status)
                .SetProperty(t => t.StrategyId, trade.StrategyId).SetProperty(t => t.EntryEmotionId, trade.EntryEmotionId)
                .SetProperty(t => t.ExitEmotionId, trade.ExitEmotionId).SetProperty(t => t.EntryReason, trade.EntryReason)
                .SetProperty(t => t.ExitReason, trade.ExitReason).SetProperty(t => t.PlanRespected, trade.PlanRespected)
                .SetProperty(t => t.Notes, trade.Notes).SetProperty(t => t.UpdatedAt, trade.UpdatedAt), ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number == 547)
        {
            throw new ValidationException("Une référence du trade n'est plus disponible.");
        }
        return TradeResponse.FromEntity(trade);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var trade = await context.Trades.FromSqlInterpolated(
            $"SELECT * FROM [Trades] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id} AND [UserId] = {userId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (trade is null) return false;
        var keys = await context.TradeImages.Where(i => i.TradeId == id && i.Trade.UserId == userId).Select(i => i.StorageKey).ToListAsync(ct);
        context.AddRange(keys.Select(key => new ImageDeletion { StorageKey = key }));
        await context.SaveChangesAsync(ct);
        await Owned(userId).Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private IQueryable<Trade> Owned(Guid userId) => context.Trades.Where(t => t.UserId == userId);
}
