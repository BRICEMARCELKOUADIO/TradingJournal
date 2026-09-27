using TradingJournal.Domain.Entities;

namespace TradingJournal.Application.Trades;

public interface ITradeRepository
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> StrategyAccessibleAsync(Guid userId, Guid strategyId, CancellationToken cancellationToken);
    Task<bool> EmotionExistsAsync(Guid emotionId, CancellationToken cancellationToken);
    Task AddAsync(Trade trade, CancellationToken cancellationToken);
    Task<TradeResponse?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);
    Task<TradePage> ListAsync(Guid userId, TradeQuery query, CancellationToken cancellationToken);
    // The callback runs under a database update lock and transaction, only for an owned trade.
    Task<TradeResponse?> UpdateAsync(Guid userId, Guid id, Func<Trade, Task> update, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
