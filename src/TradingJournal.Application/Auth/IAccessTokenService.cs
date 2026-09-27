namespace TradingJournal.Application.Auth;

public interface IAccessTokenService
{
    AccessToken Create(Guid userId);
}
