namespace TradingJournal.Application.Auth;

public static class EmailNormalizer
{
    public static string Normalize(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;
}
