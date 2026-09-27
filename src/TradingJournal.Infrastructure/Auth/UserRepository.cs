using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Application.Auth;
using TradingJournal.Domain.Entities;
using TradingJournal.Infrastructure.Persistence;

namespace TradingJournal.Infrastructure.Auth;

public sealed class UserRepository(TradingJournalDbContext context) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            context.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
