namespace TradingJournal.Api.Auth;

public sealed class AuthRateLimitOptions
{
    public int LoginPermitLimit { get; set; } = 5;
    public int RegisterPermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}
