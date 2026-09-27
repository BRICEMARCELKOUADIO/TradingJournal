using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingJournal.Domain;
using TradingJournal.Domain.Entities;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class PersistenceConfigurationTests
{
    private static TradingJournalDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TradingJournalDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True")
            .Options);

    [Theory]
    [InlineData(typeof(User), "DisplayName", "nvarchar(100)", false)]
    [InlineData(typeof(User), "Email", "nvarchar(255)", false)]
    [InlineData(typeof(User), "PasswordHash", "nvarchar(500)", false)]
    [InlineData(typeof(Trade), "Symbol", "nvarchar(30)", false)]
    [InlineData(typeof(Trade), "PositionSize", "decimal(18,6)", false)]
    [InlineData(typeof(Trade), "EntryPrice", "decimal(18,8)", false)]
    [InlineData(typeof(Trade), "ExitPrice", "decimal(18,8)", true)]
    [InlineData(typeof(Trade), "StopLoss", "decimal(18,8)", true)]
    [InlineData(typeof(Trade), "TakeProfit", "decimal(18,8)", true)]
    [InlineData(typeof(Trade), "ResultAmount", "decimal(18,2)", true)]
    [InlineData(typeof(Trade), "EntryReason", "nvarchar(1000)", true)]
    [InlineData(typeof(Trade), "ExitReason", "nvarchar(1000)", true)]
    [InlineData(typeof(Trade), "Notes", "nvarchar(2000)", true)]
    [InlineData(typeof(Strategy), "Name", "nvarchar(100)", false)]
    [InlineData(typeof(Strategy), "Description", "nvarchar(1000)", true)]
    [InlineData(typeof(Emotion), "Name", "nvarchar(50)", false)]
    [InlineData(typeof(Emotion), "Description", "nvarchar(250)", true)]
    [InlineData(typeof(TradeImage), "StorageKey", "nvarchar(500)", false)]
    [InlineData(typeof(TradeImage), "FileName", "nvarchar(255)", false)]
    [InlineData(typeof(TradeImage), "ContentType", "nvarchar(100)", false)]
    public void ColumnsHaveExpectedSqlTypesAndNullability(Type entity, string name, string sqlType, bool nullable)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(entity)!.FindProperty(name)!;
        Assert.Equal(sqlType, property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
    }

    [Fact]
    public void KeysDatesEnumsAndBooleansUseSqlServerTypesWithoutShadowProperties()
    {
        using var context = CreateContext();
        Assert.Equal(7, context.Model.GetEntityTypes().Count());
        foreach (var entity in context.Model.GetEntityTypes())
        {
            Assert.Equal("Id", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
            foreach (var property in entity.GetProperties())
            {
                Assert.False(property.IsShadowProperty());
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type == typeof(Guid)) Assert.Equal("uniqueidentifier", property.GetColumnType());
                if (type == typeof(DateTime)) Assert.Equal("datetime2", property.GetColumnType());
                if (type.IsEnum || type == typeof(int)) Assert.Equal("int", property.GetColumnType());
                if (type == typeof(bool)) Assert.Equal("bit", property.GetColumnType());
            }
        }
    }

    [Fact]
    public void RelationshipsAreExplicitAndOnlyImagesCascade()
    {
        using var context = CreateContext();
        var model = context.Model;
        var trade = model.FindEntityType(typeof(Trade))!;
        Assert.Equal(4, trade.GetForeignKeys().Count());
        foreach (var (property, principal, required) in new[]
        {
            ("UserId", typeof(User), true), ("StrategyId", typeof(Strategy), false),
            ("EntryEmotionId", typeof(Emotion), false), ("ExitEmotionId", typeof(Emotion), false)
        })
        {
            var fk = Assert.Single(trade.GetForeignKeys(), f => f.Properties.Single().Name == property);
            Assert.Equal(principal, fk.PrincipalEntityType.ClrType);
            Assert.Equal(required, fk.IsRequired);
            Assert.Equal(DeleteBehavior.ClientNoAction, fk.DeleteBehavior);
        }
        var owner = Assert.Single(model.FindEntityType(typeof(Strategy))!.GetForeignKeys());
        Assert.False(owner.IsRequired);
        Assert.Equal(DeleteBehavior.ClientNoAction, owner.DeleteBehavior);
        var image = Assert.Single(model.FindEntityType(typeof(TradeImage))!.GetForeignKeys());
        Assert.True(image.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, image.DeleteBehavior);
    }

    [Fact]
    public void RequiredIndexesExist()
    {
        using var context = CreateContext();
        var model = context.Model;
        Assert.Contains(model.FindEntityType(typeof(User))!.GetIndexes(), i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Email" }));
        var indexes = model.FindEntityType(typeof(Trade))!.GetIndexes();
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "EntryDateTime" }));
        Assert.Contains(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "MarketType" }));
        Assert.Contains(model.FindEntityType(typeof(TradeImage))!.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TradeId" }));
    }

    [Fact]
    public void MigrationMatchesModelAndSeedsTheExistingStableEmotions()
    {
        using var context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.EndsWith("_InitialCreate", migrations[0]);
        Assert.EndsWith("_AddImageUploadWorkflow", migrations[1]);
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var seed = designModel.FindEntityType(typeof(Emotion))!.GetSeedData().ToArray();
        Assert.Equal(7, seed.Length);
        foreach (var emotion in PredefinedEmotions.Create())
            Assert.Contains(seed, row => Equals(row["Id"], emotion.Id) && Equals(row["Name"], emotion.Name));
        var sql = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CK_Trades_ClosedRequiresExit", sql);
        Assert.Contains("IF NOT EXISTS", sql);
        Assert.Equal(1, sql.Split("ON DELETE CASCADE").Length - 1);
    }
}
