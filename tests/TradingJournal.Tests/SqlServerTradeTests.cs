using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TradingJournal.Application.Auth;
using TradingJournal.Application.Trades;
using TradingJournal.Domain;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class SqlServerTradeTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private async Task<HttpClient> Client(AuthApiFactory factory, Guid userId)
    {
        await using var context = fixture.CreateContext();
        context.Users.Add(new User { Id = userId, DisplayName = "Trader", Email = $"{userId:N}@example.test", PasswordHash = "test-only" });
        await context.SaveChangesAsync();
        var client = factory.CreateHttpsClient();
        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Create(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    private static async Task<TradeResponse> Create(HttpClient client, TradeWriteRequest? request = null)
    {
        using var response = await client.PostAsJsonAsync("/api/trades", request ?? TradeValidationTests.ValidRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var trade = (await response.Content.ReadFromJsonAsync<TradeResponse>())!;
        Assert.EndsWith($"/api/trades/{trade.Id}", response.Headers.Location!.ToString());
        return trade;
    }

    private static CloseTradeRequest Closing() => new()
    {
        ExitDateTime = TradeValidationTests.ValidRequest().EntryDateTime!.Value.AddHours(1),
        ExitPrice = 1.02345678m, ResultAmount = -123.45m,
        ExitEmotionId = PredefinedEmotions.Create()[2].Id, ExitReason = "Fin du plan", PlanRespected = true
    };

    [SqlServerFact]
    public async Task CreatesOpenAndClosedTradesWithoutCalculatingResult()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        var userId = Guid.NewGuid();
        using var client = await Client(factory, userId);
        var open = await Create(client);
        Assert.Equal(Status.Open, open.Status);
        Assert.Equal("EURUSD", open.Symbol);
        Assert.Null(open.ExitPrice);
        Assert.Null(open.ExitDateTime);
        Assert.Null(open.ResultAmount);
        var request = TradeValidationTests.ValidRequest();
        request.Status = Status.Closed;
        request.ExitDateTime = request.EntryDateTime;
        request.ExitPrice = 1.5m;
        request.ResultAmount = -987.65m;
        request.EntryReason = "Signal";
        request.ExitReason = "Sortie";
        request.Notes = "Journal";
        request.PlanRespected = false;
        request.EntryEmotionId = PredefinedEmotions.Create()[0].Id;
        request.ExitEmotionId = PredefinedEmotions.Create()[1].Id;
        var closed = await Create(client, request);
        Assert.Equal(Status.Closed, closed.Status);
        Assert.Equal(-987.65m, closed.ResultAmount);
        Assert.Equal(request.EntryReason, closed.EntryReason);
        Assert.Equal(request.ExitReason, closed.ExitReason);
        Assert.Equal(request.Notes, closed.Notes);
        Assert.False(closed.PlanRespected);
        Assert.Equal(request.EntryEmotionId, closed.EntryEmotionId);
        Assert.Equal(request.ExitEmotionId, closed.ExitEmotionId);
        Assert.Equal(closed, await client.GetFromJsonAsync<TradeResponse>($"/api/trades/{closed.Id}"));
        await using var context = fixture.CreateContext();
        Assert.Equal(userId, (await context.Trades.SingleAsync(t => t.Id == closed.Id)).UserId);
        var json = await client.GetStringAsync($"/api/trades/{open.Id}");
        Assert.DoesNotContain("userId", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task RejectsInvalidFieldsUnknownReferencesAndOwnerOverposting()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        var userId = Guid.NewGuid();
        using var client = await Client(factory, userId);
        var changes = new (string Key, JsonNode? Value)[]
        {
            ("symbol", ""), ("symbol", new string('X', 31)), ("notes", new string('x', 2001)),
            ("entryReason", new string('x', 1001)), ("exitReason", new string('x', 1001)),
            ("positionSize", 0), ("entryPrice", -1), ("stopLoss", 0), ("takeProfit", -1),
            ("entryPrice", 0.000000001m), ("positionSize", 0.0000001m),
            ("marketType", 99), ("direction", 99), ("status", 99),
            ("entryDateTime", null), ("entryPrice", null), ("direction", null), ("marketType", null),
            ("strategyId", Guid.NewGuid().ToString()), ("entryEmotionId", Guid.NewGuid().ToString()),
            ("exitEmotionId", Guid.NewGuid().ToString()), ("userId", userId.ToString()),
            ("exitPrice", 1m), ("status", 1)
        };
        foreach (var (key, value) in changes)
        {
            var body = JsonSerializer.SerializeToNode(TradeValidationTests.ValidRequest(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            body[key] = value;
            var response = await client.PostAsJsonAsync("/api/trades", body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{key}: {await response.Content.ReadAsStringAsync()}");
        }
        var backdated = TradeValidationTests.ValidRequest();
        backdated.Status = Status.Closed;
        backdated.ExitDateTime = backdated.EntryDateTime!.Value.AddSeconds(-1);
        backdated.ExitPrice = 1m;
        backdated.ResultAmount = 0m;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/trades", backdated)).StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.Trades.CountAsync(t => t.UserId == userId));
    }

    [SqlServerFact]
    public async Task ClosesTradeAndRejectsInvalidAndRepeatedCloseWithoutChangingIt()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var client = await Client(factory, Guid.NewGuid());
        var request = TradeValidationTests.ValidRequest();
        request.Notes = "Notes à conserver";
        var trade = await Create(client, request);
        var path = $"/api/trades/{trade.Id}/close";
        var invalid = Closing();
        invalid.ExitDateTime = request.EntryDateTime!.Value.AddDays(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, invalid)).StatusCode);
        invalid = Closing();
        invalid.ExitEmotionId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, invalid)).StatusCode);
        invalid = Closing();
        invalid.ResultAmount = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, invalid)).StatusCode);
        Assert.Equal(Status.Open, (await client.GetFromJsonAsync<TradeResponse>($"/api/trades/{trade.Id}"))!.Status);
        var response = await client.PostAsJsonAsync(path, Closing());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<TradeResponse>())!;
        Assert.Equal(Status.Closed, closed.Status);
        Assert.Equal(-123.45m, closed.ResultAmount);
        Assert.Equal(request.Notes, closed.Notes);
        Assert.Equal(Closing().ExitEmotionId, closed.ExitEmotionId);
        Assert.True(closed.PlanRespected);
        Assert.NotNull(closed.UpdatedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, Closing())).StatusCode);
        Assert.Equal(closed, await client.GetFromJsonAsync<TradeResponse>($"/api/trades/{trade.Id}"));
    }

    [SqlServerFact]
    public async Task ConcurrentCloseOnlySucceedsOnce()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var client = await Client(factory, Guid.NewGuid());
        var trade = await Create(client);
        var path = $"/api/trades/{trade.Id}/close";
        var results = await Task.WhenAll(client.PostAsJsonAsync(path, Closing()), client.PostAsJsonAsync(path, Closing()));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [SqlServerFact]
    public async Task UpdatesThenDeletesWithoutLosingImagesBeforeDeletion()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var client = await Client(factory, Guid.NewGuid());
        var trade = await Create(client);
        var path = $"/api/trades/{trade.Id}";
        await using var context = fixture.CreateContext();
        context.TradeImages.Add(new TradeImage { TradeId = trade.Id, StorageKey = "future/chart", FileName = "chart.png", ContentType = "image/png" });
        await context.SaveChangesAsync();
        var request = TradeValidationTests.ValidRequest();
        request.Symbol = "aapl";
        request.MarketType = MarketType.Stock;
        request.Direction = Direction.Sell;
        request.PositionSize = 10m;
        request.EntryPrice = 200m;
        request.Notes = "Mise à jour";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, request)).StatusCode);
        var updated = (await client.GetFromJsonAsync<TradeResponse>(path))!;
        Assert.Equal("AAPL", updated.Symbol);
        Assert.Equal(MarketType.Stock, updated.MarketType);
        Assert.Equal(Direction.Sell, updated.Direction);
        Assert.Equal(10m, updated.PositionSize);
        Assert.Equal(200m, updated.EntryPrice);
        Assert.Equal(request.Notes, updated.Notes);
        Assert.Equal(trade.CreatedAt, updated.CreatedAt);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(1, await context.TradeImages.CountAsync(i => i.TradeId == trade.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path)).StatusCode);
        Assert.Equal(0, await context.TradeImages.CountAsync(i => i.TradeId == trade.Id));
    }

    [SqlServerFact]
    public async Task PutCanCloseButCannotReopenOrRewriteClosedExitData()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var client = await Client(factory, Guid.NewGuid());
        var trade = await Create(client);
        var path = $"/api/trades/{trade.Id}";
        var request = TradeValidationTests.ValidRequest();
        request.Status = Status.Closed;
        request.ExitDateTime = request.EntryDateTime;
        request.ExitPrice = 2m;
        request.ResultAmount = 0m;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, request)).StatusCode);
        request.Notes = "Annotation après clôture";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, TradeValidationTests.ValidRequest())).StatusCode);
        request.ResultAmount = 2m;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, request)).StatusCode);
        request.ResultAmount = 0m;
        request.EntryDateTime = request.ExitDateTime!.Value.AddSeconds(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, request)).StatusCode);
        var retained = (await client.GetFromJsonAsync<TradeResponse>(path))!;
        Assert.Equal(Status.Closed, retained.Status);
        Assert.Equal(0m, retained.ResultAmount);
        Assert.Equal("Annotation après clôture", retained.Notes);
    }

    [SqlServerFact]
    public async Task StrategiesMustBeGlobalOrOwnedAndOwnershipIsolatedForAllOperations()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        using var owner = await Client(factory, ownerId);
        using var other = await Client(factory, otherId);
        await using var context = fixture.CreateContext();
        var personal = new Strategy { UserId = ownerId, Name = "Personnelle" };
        var global = new Strategy { Name = "Globale" };
        context.AddRange(personal, global);
        await context.SaveChangesAsync();
        var request = TradeValidationTests.ValidRequest();
        request.StrategyId = personal.Id;
        var owned = await Create(owner, request);
        Assert.Equal(personal.Id, owned.StrategyId);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/trades", request)).StatusCode);
        request.StrategyId = global.Id;
        var others = await Create(other, request);
        Assert.Equal(global.Id, others.StrategyId);
        request.StrategyId = personal.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync($"/api/trades/{others.Id}", request)).StatusCode);
        foreach (var id in new[] { owned.Id, Guid.NewGuid() })
        {
            var path = $"/api/trades/{id}";
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync(path, TradeValidationTests.ValidRequest())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(path + "/close", Closing())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(path)).StatusCode);
        }
        var ownPage = (await owner.GetFromJsonAsync<TradePage>("/api/trades"))!;
        var otherPage = (await other.GetFromJsonAsync<TradePage>($"/api/trades?userId={ownerId}"))!;
        Assert.Equal(owned.Id, Assert.Single(ownPage.Items).Id);
        Assert.Equal(others.Id, Assert.Single(otherPage.Items).Id);
        Assert.Equal(owned, await owner.GetFromJsonAsync<TradeResponse>($"/api/trades/{owned.Id}"));
    }

    [SqlServerFact]
    public async Task FiltersPaginationAndStableOrderingExecuteOnSqlServer()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        var userId = Guid.NewGuid();
        using var client = await Client(factory, userId);
        await using (var context = fixture.CreateContext())
        {
            for (var i = 0; i < 25; i++)
            {
                var trade = new Trade
                {
                    UserId = userId, Symbol = i % 2 == 0 ? "EURUSD" : "AAPL",
                    MarketType = i % 2 == 0 ? MarketType.Forex : MarketType.Stock,
                    Direction = i % 2 == 0 ? Direction.Buy : Direction.Sell,
                    EntryDateTime = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i / 2),
                    EntryPrice = 1m, PositionSize = 1m
                };
                if (i % 2 == 0) trade.Close(trade.EntryDateTime, 2m, 1m);
                context.Trades.Add(trade);
            }
            await context.SaveChangesAsync();
        }
        var first = (await client.GetFromJsonAsync<TradePage>("/api/trades"))!;
        var second = (await client.GetFromJsonAsync<TradePage>("/api/trades?page=2"))!;
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        var combined = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(25, combined.Select(t => t.Id).Distinct().Count());
        Assert.Equal(combined.OrderByDescending(t => t.EntryDateTime).Select(t => t.EntryDateTime), combined.Select(t => t.EntryDateTime));
        // SQL Server's uniqueidentifier ordering differs from the CLR Guid comparer.
        Assert.Equal(first.Items.Select(t => t.Id), (await client.GetFromJsonAsync<TradePage>("/api/trades"))!.Items.Select(t => t.Id));
        foreach (var filter in new[] { "marketType=Forex", "symbol=eurusd", "status=Closed", "direction=Buy" })
        {
            var filtered = (await client.GetFromJsonAsync<TradePage>("/api/trades?" + filter))!;
            Assert.Equal(13, filtered.TotalCount);
            Assert.All(filtered.Items, item => Assert.Equal("EURUSD", item.Symbol));
        }
        var bounds = (await client.GetFromJsonAsync<TradePage>("/api/trades?startDate=2026-09-03T00:00:00Z&endDate=2026-09-05T00:00:00Z&marketType=Forex&symbol=EURUSD&status=Closed&direction=Buy"))!;
        Assert.Equal(3, bounds.TotalCount);
        Assert.Empty((await client.GetFromJsonAsync<TradePage>("/api/trades?page=50"))!.Items);
        foreach (var query in new[] { "page=0", "pageSize=101", "pageSize=0", "page=2147483647&pageSize=100", "marketType=99", "status=99", "direction=99", "startDate=2026-09-10&endDate=2026-09-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/trades?" + query)).StatusCode);

        var sql = new SqlCapture();
        await using var observed = new TradingJournalDbContext(new DbContextOptionsBuilder<TradingJournalDbContext>()
            .UseSqlServer(fixture.ConnectionString).AddInterceptors(sql).Options);
        var repository = new TradeRepository(observed);
        var page = await repository.ListAsync(userId, new TradeQuery { Page = 2, PageSize = 3 }, default);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(2, sql.Commands.Count);
        Assert.All(sql.Commands, command => Assert.Contains("[UserId]", command));
        Assert.Contains(sql.Commands, command => command.Contains("OFFSET") && command.Contains("FETCH NEXT"));
        Assert.Empty(observed.ChangeTracker.Entries());
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
