namespace TradingJournal.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 15;

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience)
            || LifetimeMinutes is < 1 or > 60) return false;
        try { return Convert.FromBase64String(SigningKey).Length is >= 32 and <= 64; }
        catch (FormatException) { return false; }
    }
}
