using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Domain;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class SqlServerPersistenceTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static User NewUser() => new()
    {
        DisplayName = "Test", Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "test-only-hash"
    };

    private static Trade NewTrade() => new()
    {
        User = NewUser(), Symbol = "EURUSD", MarketType = MarketType.Forex, Direction = Direction.Buy,
        EntryDateTime = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc),
        EntryPrice = 1.12345678m, PositionSize = 0.123456m,
        StopLoss = 1.02345678m, TakeProfit = 1.22345678m
    };

    [SqlServerFact]
    public async Task MigrationCreatesFiveTablesAndRepeatedMigrationDoesNotDuplicateEmotions()
    {
        await using var context = fixture.CreateContext();
        var tables = await context.Database.SqlQueryRaw<string>(
            "SELECT [name] AS [Value] FROM sys.tables WHERE [name] <> '__EFMigrationsHistory'")
            .ToListAsync();
        Assert.Equal(new[] { "Emotions", "ImageDeletions", "ImageUploads", "Strategies", "TradeImages", "Trades", "Users" }, tables.Order());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_InitialCreate", migrations[0]);
        Assert.EndsWith("_AddImageUploadWorkflow", migrations[1]);
        var actual = await context.Emotions.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(PredefinedEmotions.Create().Select(e => (e.Id, e.Name)), actual.Select(e => (e.Id, e.Name)));
    }

    [SqlServerFact]
    public async Task SqlServerRejectsDuplicateEmails()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var first = NewUser();
        context.Users.Add(first);
        await context.SaveChangesAsync();
        var duplicate = NewUser();
        duplicate.Email = first.Email;
        context.Users.Add(duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, new[] { 2601, 2627 });
    }

    [SqlServerFact]
    public async Task SqlServerRejectsMissingRequiredValueAndOverlongText()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        context.Add(user);
        await context.SaveChangesAsync();
        var missing = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Users] SET [DisplayName] = NULL WHERE [Id] = {user.Id}"));
        Assert.Equal(515, missing.Number);
        var tooLong = new string('x', 101);
        var length = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Users] SET [DisplayName] = {tooLong} WHERE [Id] = {user.Id}"));
        Assert.Contains(length.Number, new[] { 2628, 8152 });
    }

    [SqlServerFact]
    public async Task OpenAndClosedTradesRoundTripWithDecimalPrecisionAndOptionalRelationships()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var trade = NewTrade();
        context.Add(trade);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var loaded = await context.Trades.SingleAsync(t => t.Id == trade.Id);
        Assert.Equal(Status.Open, loaded.Status);
        Assert.Null(loaded.ExitDateTime);
        Assert.Null(loaded.ExitPrice);
        Assert.Null(loaded.ResultAmount);
        Assert.Null(loaded.StrategyId);
        Assert.Null(loaded.EntryEmotionId);
        Assert.Null(loaded.ExitEmotionId);
        loaded.Strategy = new Strategy { Name = "Globale" };
        // Domain entities already have a Guid: explicitly mark this new principal for insertion.
        context.Strategies.Add(loaded.Strategy);
        loaded.EntryEmotionId = PredefinedEmotions.Create()[0].Id;
        loaded.ExitEmotionId = PredefinedEmotions.Create()[2].Id;
        var exitDate = loaded.EntryDateTime.AddHours(1);
        loaded.Close(exitDate, 1.11345678m, -12.34m);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var closed = await context.Trades.Include(t => t.Strategy).Include(t => t.EntryEmotion)
            .Include(t => t.ExitEmotion).SingleAsync(t => t.Id == trade.Id);
        Assert.Equal(Status.Closed, closed.Status);
        Assert.Equal(exitDate, closed.ExitDateTime);
        Assert.Equal(1.12345678m, closed.EntryPrice);
        Assert.Equal(0.123456m, closed.PositionSize);
        Assert.Equal(1.02345678m, closed.StopLoss);
        Assert.Equal(1.22345678m, closed.TakeProfit);
        Assert.Equal(1.11345678m, closed.ExitPrice);
        Assert.Equal(-12.34m, closed.ResultAmount);
        Assert.NotNull(closed.UpdatedAt);
        Assert.Equal("Calme", closed.EntryEmotion!.Name);
        Assert.Equal("Stressé", closed.ExitEmotion!.Name);
        Assert.Null(closed.Strategy!.UserId);
    }

    [SqlServerFact]
    public async Task SqlServerRejectsEveryIncompleteCloseEvenWhenBypassingDomainMethod()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var trade = NewTrade();
        context.Add(trade);
        await context.SaveChangesAsync();
        foreach (var missing in new[] { "date", "price", "result" })
        {
            DateTime? date = missing == "date" ? null : trade.EntryDateTime.AddHours(1);
            decimal? price = missing == "price" ? null : 1.2m;
            decimal? result = missing == "result" ? null : 0m;
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Trades] SET [Status] = 1, [ExitDateTime] = {date}, [ExitPrice] = {price}, [ResultAmount] = {result} WHERE [Id] = {trade.Id}"));
            Assert.Equal(547, error.Number);
        }
        var invalidStatus = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Trades] SET [Status] = 99 WHERE [Id] = {trade.Id}"));
        Assert.Equal(547, invalidStatus.Number);
        context.ChangeTracker.Clear();
        Assert.Equal(Status.Open, (await context.Trades.SingleAsync(t => t.Id == trade.Id)).Status);
    }

    [SqlServerFact]
    public async Task SqlServerRejectsOrphanTrade()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var trade = NewTrade();
        trade.User = null!;
        trade.UserId = Guid.NewGuid();
        context.Add(trade);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [SqlServerFact]
    public async Task ReferencedUserStrategyAndBothEmotionsCannotBeDeletedWithTrackedTrades()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var trade = NewTrade();
        trade.Strategy = new Strategy { Name = "Personnelle", User = trade.User };
        trade.EntryEmotion = new Emotion { Name = "Entrée test" };
        trade.ExitEmotion = new Emotion { Name = "Sortie test" };
        context.Add(trade);
        await context.SaveChangesAsync();
        foreach (var principal in new object[] { trade.User, trade.Strategy, trade.EntryEmotion, trade.ExitEmotion })
        {
            context.Remove(principal);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
            context.Entry(principal).State = EntityState.Unchanged;
        }
        context.ChangeTracker.Clear();
        var preserved = await context.Trades.SingleAsync(t => t.Id == trade.Id);
        Assert.NotNull(preserved.StrategyId);
        Assert.NotNull(preserved.EntryEmotionId);
        Assert.NotNull(preserved.ExitEmotionId);
    }

    [SqlServerFact]
    public async Task DeletingTradeCascadesToUnloadedImageRowsOnly()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var trade = NewTrade();
        trade.Strategy = new Strategy { Name = "Globale" };
        trade.EntryEmotionId = PredefinedEmotions.Create()[0].Id;
        foreach (var type in new[] { ImageType.Before, ImageType.Before, ImageType.After })
            trade.Images.Add(new TradeImage
            {
                StorageKey = $"test/{Guid.NewGuid():N}", FileName = "chart.png", ContentType = "image/png", ImageType = type
            });
        context.Add(trade);
        await context.SaveChangesAsync();
        Assert.Equal(3, await context.TradeImages.CountAsync(i => i.TradeId == trade.Id));
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.Trades.Where(t => t.Id == trade.Id).ExecuteDeleteAsync());
        Assert.Equal(0, await context.TradeImages.CountAsync(i => i.TradeId == trade.Id));
        Assert.True(await context.Users.AnyAsync(u => u.Id == trade.UserId));
        Assert.True(await context.Strategies.AnyAsync(s => s.Id == trade.StrategyId));
        Assert.True(await context.Emotions.AnyAsync(e => e.Id == trade.EntryEmotionId));
    }
}
