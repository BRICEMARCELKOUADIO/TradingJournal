using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Application.Auth;
using TradingJournal.Application.Trades;

namespace TradingJournal.Api.Controllers;

[ApiController, Authorize, Route("api/trades"), TradeExceptionFilter]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(32_768)]
public sealed class TradesController(TradeService trades, ICurrentUser currentUser) : ControllerBase
{
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();

    [HttpPost]
    public async Task<ActionResult<TradeResponse>> Create(TradeWriteRequest request, CancellationToken ct)
    {
        var trade = await trades.CreateAsync(UserId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = trade.Id }, trade);
    }

    [HttpGet]
    public async Task<ActionResult<TradePage>> List([FromQuery] TradeQuery query, CancellationToken ct) =>
        Ok(await trades.ListAsync(UserId, query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TradeResponse>> Get(Guid id, CancellationToken ct)
    {
        var trade = await trades.GetAsync(UserId, id, ct);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TradeResponse>> Update(Guid id, TradeWriteRequest request, CancellationToken ct)
    {
        var trade = await trades.UpdateAsync(UserId, id, request, ct);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<TradeResponse>> Close(Guid id, CloseTradeRequest request, CancellationToken ct)
    {
        var trade = await trades.CloseAsync(UserId, id, request, ct);
        return trade is null ? NotFound() : Ok(trade);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await trades.DeleteAsync(UserId, id, ct) ? NoContent() : NotFound();
}
