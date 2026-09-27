using System.Net;
using System.Net.Http.Json;
using TradingJournal.Application.Trades;
using Xunit;

namespace TradingJournal.Tests;

public sealed class TradeAuthorizationTests
{
    [Fact]
    public async Task EveryTradeEndpointRejectsRequestsWithoutJwt()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        var path = $"/api/trades/{Guid.NewGuid()}";
        var results = new[]
        {
            await client.GetAsync("/api/trades"), await client.GetAsync(path),
            await client.PostAsJsonAsync("/api/trades", TradeValidationTests.ValidRequest()),
            await client.PutAsJsonAsync(path, TradeValidationTests.ValidRequest()),
            await client.PostAsJsonAsync(path + "/close", new CloseTradeRequest()),
            await client.DeleteAsync(path)
        };
        Assert.All(results, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        foreach (var response in results) response.Dispose();
    }
}
