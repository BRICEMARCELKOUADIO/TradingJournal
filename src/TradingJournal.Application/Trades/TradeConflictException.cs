namespace TradingJournal.Application.Trades;

public sealed class TradeConflictException(string message) : Exception(message);
