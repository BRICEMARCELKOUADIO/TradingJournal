namespace TradingJournal.Application.Images;

public interface ITradeImageStorage
{
    Task<SignedImageUrl> SignUploadAsync(ImageUploadReservation upload, DateTime expiresAt, CancellationToken ct);
    Task<UploadedImage?> ReadUploadAsync(string key, CancellationToken ct);
    Task WriteValidatedAsync(string key, byte[] bytes, string contentType, CancellationToken ct);
    Task<SignedImageUrl> SignDownloadAsync(string key, string contentType, DateTime expiresAt, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public interface ITradeImageRepository
{
    Task<ImageUploadReservation> ReserveAsync(Guid userId, Guid tradeId, RequestImageUpload request, DateTime expiresAt, int maximum, CancellationToken ct);
    Task<TradeImageResponse> ConfirmAsync(Guid userId, Guid tradeId, Guid uploadId,
        Func<ImageUploadReservation, Task> validateAndStore, int maximum, CancellationToken ct);
    Task<IReadOnlyList<TradeImageResponse>> ListAsync(Guid userId, Guid tradeId, CancellationToken ct);
    Task<StoredTradeImage> GetAsync(Guid userId, Guid tradeId, Guid imageId, CancellationToken ct);
    Task DeleteAsync(Guid userId, Guid tradeId, Guid imageId, CancellationToken ct);
}
