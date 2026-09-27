using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingJournal.Application.Auth;
using TradingJournal.Application.Images;

namespace TradingJournal.Api.Controllers;

[ApiController, Authorize, Route("api/trades/{tradeId:guid}/images"), TradeExceptionFilter]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(8192)]
public sealed class TradeImagesController(TradeImageService images, ICurrentUser currentUser) : ControllerBase
{
    private Guid OwnerId => currentUser.UserId ?? throw new UnauthorizedAccessException();
    [HttpPost("uploads")]
    public async Task<ActionResult<UploadImageResponse>> RequestUpload(Guid tradeId, RequestImageUpload request, CancellationToken ct) =>
        Ok(await images.RequestAsync(OwnerId, tradeId, request, ct));
    [HttpPost("uploads/{uploadId:guid}/confirm")]
    public async Task<ActionResult<TradeImageResponse>> Confirm(Guid tradeId, Guid uploadId, CancellationToken ct) =>
        Ok(await images.ConfirmAsync(OwnerId, tradeId, uploadId, ct));
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TradeImageResponse>>> List(Guid tradeId, CancellationToken ct) =>
        Ok(await images.ListAsync(OwnerId, tradeId, ct));
    [HttpGet("{imageId:guid}/download-url")]
    public async Task<ActionResult<SignedImageUrl>> Download(Guid tradeId, Guid imageId, CancellationToken ct) =>
        Ok(await images.DownloadAsync(OwnerId, tradeId, imageId, ct));
    [HttpDelete("{imageId:guid}")]
    public async Task<IActionResult> Delete(Guid tradeId, Guid imageId, CancellationToken ct)
    {
        await images.DeleteAsync(OwnerId, tradeId, imageId, ct);
        return NoContent();
    }
}
