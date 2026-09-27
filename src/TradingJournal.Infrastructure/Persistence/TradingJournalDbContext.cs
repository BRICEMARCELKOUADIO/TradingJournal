using Microsoft.EntityFrameworkCore;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Infrastructure.Persistence;

public sealed class TradingJournalDbContext(
    DbContextOptions<TradingJournalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<Emotion> Emotions => Set<Emotion>();
    public DbSet<TradeImage> TradeImages => Set<TradeImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingJournalDbContext).Assembly);
    }
}
