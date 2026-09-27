using System.ComponentModel.DataAnnotations;
using TradingJournal.Application.Trades;
using TradingJournal.Domain.Enums;
using Xunit;

namespace TradingJournal.Tests;

public sealed class TradeValidationTests
{
    public static TradeWriteRequest ValidRequest() => new()
    {
        MarketType = MarketType.Forex, Symbol = "eurusd", Direction = Direction.Buy,
        PositionSize = 0.123456m, EntryPrice = 1.12345678m,
        EntryDateTime = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero)
    };

    public static IReadOnlyList<ValidationResult> Errors(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.00000001", true)]
    [InlineData("0.000000001", false)]
    [InlineData("9999999999.99999999", true)]
    [InlineData("10000000000", false)]
    public void PricesMustBePositiveAndExactlyRepresentable(string input, bool valid)
    {
        var request = ValidRequest();
        request.EntryPrice = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(valid, Errors(request).Count == 0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-125.25")]
    [InlineData("125.25")]
    public void ClosedTradeAcceptsEnteredMonetaryProfitLossAndZero(string result)
    {
        var request = ValidRequest();
        request.Status = Status.Closed;
        request.ExitDateTime = request.EntryDateTime;
        request.ExitPrice = 1m;
        request.ResultAmount = decimal.Parse(result, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Empty(Errors(request));
    }

    [Fact]
    public void ClosedTradeNeedsEveryExitFieldAndChronologicalDates()
    {
        var request = ValidRequest();
        request.Status = Status.Closed;
        Assert.NotEmpty(Errors(request));
        request.ExitDateTime = request.EntryDateTime!.Value.AddSeconds(-1);
        request.ExitPrice = 1m;
        request.ResultAmount = 0m;
        Assert.NotEmpty(Errors(request));
        request.ExitDateTime = request.EntryDateTime;
        Assert.Empty(Errors(request));
        request.ResultAmount = 0.001m;
        Assert.NotEmpty(Errors(request));
    }

    [Fact]
    public void QuantityStopsAndEnumsAreValidated()
    {
        var request = ValidRequest();
        request.PositionSize = 0.0000001m;
        request.StopLoss = -1m;
        request.TakeProfit = 0m;
        request.MarketType = (MarketType)99;
        request.Direction = (Direction)99;
        request.Status = (Status)99;
        Assert.Equal(6, Errors(request).Count);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void InvalidPaginationIsRejected(int page, int size) =>
        Assert.NotEmpty(Errors(new TradeQuery { Page = page, PageSize = size }));

    [Fact]
    public void QueryDefaultsAndDateBoundsAreValidated()
    {
        var query = new TradeQuery();
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Empty(Errors(query));
        query.StartDate = DateTimeOffset.UtcNow;
        query.EndDate = query.StartDate.Value.AddDays(-1);
        Assert.NotEmpty(Errors(query));
    }
}
