using System.ComponentModel.DataAnnotations;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Application.Trades;

public sealed class TradeService(ITradeRepository repository)
{
    public async Task<TradeResponse> CreateAsync(Guid userId, TradeWriteRequest request, CancellationToken ct = default)
    {
        Validate(request);
        if (!await repository.UserExistsAsync(userId, ct)) throw new UnauthorizedAccessException();
        await ValidateReferences(userId, request.StrategyId, request.EntryEmotionId, request.ExitEmotionId, ct);
        var trade = new Trade { UserId = userId };
        Apply(trade, request);
        if (request.Status == Status.Closed)
            trade.Close(request.ExitDateTime!.Value.UtcDateTime, request.ExitPrice, request.ResultAmount);
        await repository.AddAsync(trade, ct);
        return TradeResponse.FromEntity(trade);
    }

    public Task<TradeResponse?> GetAsync(Guid userId, Guid id, CancellationToken ct = default) => repository.GetAsync(userId, id, ct);

    public Task<TradePage> ListAsync(Guid userId, TradeQuery query, CancellationToken ct = default)
    {
        Validate(query);
        return repository.ListAsync(userId, query, ct);
    }

    public Task<TradeResponse?> UpdateAsync(Guid userId, Guid id, TradeWriteRequest request, CancellationToken ct = default) =>
        repository.UpdateAsync(userId, id, async trade =>
        {
            Validate(request);
            if (trade.Status == Status.Closed && (request.Status != Status.Closed
                || request.ExitDateTime!.Value.UtcDateTime != trade.ExitDateTime
                || request.ExitPrice != trade.ExitPrice || request.ResultAmount != trade.ResultAmount))
                throw new TradeConflictException("Un trade clôturé ne peut être rouvert ni ses données de clôture remplacées.");
            await ValidateReferences(userId, request.StrategyId, request.EntryEmotionId, request.ExitEmotionId, ct);
            Apply(trade, request);
            if (trade.Status == Status.Open && request.Status == Status.Closed)
                trade.Close(request.ExitDateTime!.Value.UtcDateTime, request.ExitPrice, request.ResultAmount);
            trade.MarkUpdated();
        }, ct);

    public Task<TradeResponse?> CloseAsync(Guid userId, Guid id, CloseTradeRequest request, CancellationToken ct = default) =>
        repository.UpdateAsync(userId, id, async trade =>
        {
            if (trade.Status == Status.Closed) throw new TradeConflictException("Le trade est déjà clôturé.");
            Validate(request);
            if (request.ExitDateTime!.Value.UtcDateTime < trade.EntryDateTime)
                throw new ValidationException("La sortie ne peut pas précéder l'entrée.");
            if (request.ExitEmotionId is Guid emotion && !await repository.EmotionExistsAsync(emotion, ct))
                throw new ValidationException("Émotion de sortie invalide.");
            trade.Close(request.ExitDateTime.Value.UtcDateTime, request.ExitPrice, request.ResultAmount);
            // Omitted optional exit annotations preserve existing journal notes.
            if (request.ExitEmotionId.HasValue) trade.ExitEmotionId = request.ExitEmotionId;
            if (request.ExitReason is not null) trade.ExitReason = request.ExitReason;
            if (request.PlanRespected.HasValue) trade.PlanRespected = request.PlanRespected;
            if (request.Notes is not null) trade.Notes = request.Notes;
        }, ct);

    public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) => repository.DeleteAsync(userId, id, ct);

    private async Task ValidateReferences(Guid userId, Guid? strategy, Guid? entryEmotion, Guid? exitEmotion, CancellationToken ct)
    {
        if (strategy is Guid strategyId && !await repository.StrategyAccessibleAsync(userId, strategyId, ct))
            throw new ValidationException("Stratégie indisponible.");
        foreach (var emotionId in new[] { entryEmotion, exitEmotion }.OfType<Guid>().Distinct())
            if (!await repository.EmotionExistsAsync(emotionId, ct)) throw new ValidationException("Émotion invalide.");
    }

    private static void Validate(object request) => Validator.ValidateObject(request, new ValidationContext(request), true);

    private static void Apply(Trade trade, TradeWriteRequest request)
    {
        trade.MarketType = request.MarketType!.Value;
        trade.Symbol = request.Symbol;
        trade.Direction = request.Direction!.Value;
        trade.PositionSize = request.PositionSize!.Value;
        trade.EntryDateTime = request.EntryDateTime!.Value.UtcDateTime;
        trade.EntryPrice = request.EntryPrice!.Value;
        trade.StopLoss = request.StopLoss;
        trade.TakeProfit = request.TakeProfit;
        trade.StrategyId = request.StrategyId;
        trade.EntryEmotionId = request.EntryEmotionId;
        trade.ExitEmotionId = request.ExitEmotionId;
        trade.EntryReason = request.EntryReason;
        trade.ExitReason = request.ExitReason;
        trade.PlanRespected = request.PlanRespected;
        trade.Notes = request.Notes;
    }
}
