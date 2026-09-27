namespace TradingJournal.Application.Auth;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
