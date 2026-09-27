namespace TradingJournal.Application.Auth;

public sealed record UserResponse(Guid Id, string DisplayName, string Email);
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, UserResponse User);
