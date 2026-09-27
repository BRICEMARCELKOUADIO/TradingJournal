using Microsoft.EntityFrameworkCore;
using TradingJournal.Domain;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;
using Xunit;

namespace TradingJournal.Tests;

public sealed class DomainTests
{
    [Fact]
    public void NewTradeIsOpenWithoutExitInformation()
    {
        var trade = new Trade();
        Assert.Equal(Status.Open, trade.Status);
        Assert.Null(trade.ExitDateTime);
        Assert.Null(trade.ExitPrice);
        Assert.Null(trade.ResultAmount);
        Assert.Null(trade.UpdatedAt);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void CloseRequiresAllExitInformationAndDoesNotPartiallyMutate(
        bool missingDate, bool missingPrice, bool missingResult)
    {
        var trade = new Trade();
        Assert.Throws<ArgumentNullException>(() => trade.Close(
            missingDate ? null : DateTime.UtcNow,
            missingPrice ? null : 1.23456m,
            missingResult ? null : 0m));
        Assert.Equal(Status.Open, trade.Status);
        Assert.Null(trade.ExitDateTime);
        Assert.Null(trade.ExitPrice);
        Assert.Null(trade.ResultAmount);
        Assert.Null(trade.UpdatedAt);
    }

    [Theory]
    [InlineData("125.12345678")]
    [InlineData("-35.12345678")]
    [InlineData("0")]
    public void ClosePreservesDecimalPrecisionAndAcceptsProfitLossOrBreakEven(string amount)
    {
        var result = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        var trade = new Trade();
        var exitDate = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        trade.Close(exitDate, 1.12345678m, result);
        Assert.Equal(Status.Closed, trade.Status);
        Assert.Equal(exitDate, trade.ExitDateTime);
        Assert.Equal(1.12345678m, trade.ExitPrice);
        Assert.Equal(result, trade.ResultAmount);
        Assert.NotNull(trade.UpdatedAt);
    }

    [Fact]
    public void ClosedTradeCannotBeClosedAgain()
    {
        var trade = new Trade();
        var exitDate = DateTime.UtcNow;
        trade.Close(exitDate, 10m, -2m);
        var updatedAt = trade.UpdatedAt;
        Assert.Throws<InvalidOperationException>(() => trade.Close(exitDate.AddDays(1), 20m, 8m));
        Assert.Equal(exitDate, trade.ExitDateTime);
        Assert.Equal(10m, trade.ExitPrice);
        Assert.Equal(-2m, trade.ResultAmount);
        Assert.Equal(updatedAt, trade.UpdatedAt);
    }

    [Fact]
    public void StrategyCanBeGlobalOrOwnedByAUser()
    {
        var predefined = new Strategy { Name = "Prédéfinie" };
        var user = new User();
        var personal = new Strategy { UserId = user.Id, User = user, Name = "Personnelle" };
        Assert.Null(predefined.UserId);
        Assert.Null(predefined.User);
        Assert.Equal(user.Id, personal.UserId);
        Assert.Same(user, personal.User);
    }

    [Fact]
    public void PredefinedEmotionsHaveStableDistinctIdsAndIndependentInstances()
    {
        var first = PredefinedEmotions.Create();
        var second = PredefinedEmotions.Create();
        Assert.Equal(new[] { "Calme", "Confiant", "Stressé", "Impatient", "Euphorique", "Frustré", "Craintif" },
            first.Select(e => e.Name));
        Assert.Equal(7, first.Select(e => e.Id).Distinct().Count());
        Assert.All(first, e => Assert.NotEqual(Guid.Empty, e.Id));
        Assert.Equal(first.Select(e => e.Id), second.Select(e => e.Id));
        first[0].Name = "Modifiée";
        Assert.Equal("Calme", second[0].Name);
    }

    [Fact]
    public void EfCoreMapsRelationshipsAndTracksMultipleImagesWithoutDatabaseAccess()
    {
        using var context = new DomainModelContext();
        var emotions = PredefinedEmotions.Create();
        var user = new User();
        var strategy = new Strategy { Name = "Globale" };
        var trade = new Trade
        {
            User = user, Strategy = strategy,
            EntryEmotion = emotions[0], ExitEmotion = emotions[2]
        };
        trade.Images.Add(new TradeImage { ImageType = ImageType.Before });
        trade.Images.Add(new TradeImage { ImageType = ImageType.Before });
        trade.Images.Add(new TradeImage { ImageType = ImageType.After });
        context.Add(trade);

        Assert.Equal(5, context.Model.GetEntityTypes().Count());
        Assert.Equal(user.Id, trade.UserId);
        Assert.Equal(strategy.Id, trade.StrategyId);
        Assert.Equal(emotions[0].Id, trade.EntryEmotionId);
        Assert.Equal(emotions[2].Id, trade.ExitEmotionId);
        Assert.Contains(trade, user.Trades);
        Assert.Contains(trade, strategy.Trades);
        Assert.Equal(3, trade.Images.Count);
        Assert.All(trade.Images, image =>
        {
            Assert.Equal(trade.Id, image.TradeId);
            Assert.Same(trade, image.Trade);
        });
        var tradeType = context.Model.FindEntityType(typeof(Trade))!;
        Assert.Equal(4, tradeType.GetForeignKeys().Count());
        Assert.DoesNotContain(tradeType.GetProperties(), p => p.IsShadowProperty());
        Assert.False(tradeType.FindProperty(nameof(Trade.EntryPrice))!.IsNullable);
        Assert.True(tradeType.FindProperty(nameof(Trade.ExitPrice))!.IsNullable);
        // Exercise EF's materialization setters without opening a SQL connection.
        context.Entry(trade).Property(t => t.ExitDateTime).CurrentValue = DateTime.UtcNow;
        context.Entry(trade).Property(t => t.ExitPrice).CurrentValue = 1m;
        context.Entry(trade).Property(t => t.ResultAmount).CurrentValue = 0m;
        context.Entry(trade).Property(t => t.Status).CurrentValue = Status.Closed;
        Assert.Equal(Status.Closed, trade.Status);
    }

    private sealed class DomainModelContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DomainModelTests;Trusted_Connection=True");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Trade>();
    }
}
