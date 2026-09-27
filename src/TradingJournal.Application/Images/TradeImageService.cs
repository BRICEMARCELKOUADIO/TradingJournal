using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Application.Images;

public sealed class TradeImageService(ITradeImageRepository repository, ITradeImageStorage storage, ImageOptions options, TimeProvider time)
{
    public async Task<UploadImageResponse> RequestAsync(Guid user, Guid trade, RequestImageUpload request, CancellationToken ct)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        var now = time.GetUtcNow().UtcDateTime;
        var reservation = await repository.ReserveAsync(user, trade, request, now.AddMinutes(options.UploadLifetimeMinutes), options.MaxImagesPerTrade, ct);
        var signed = await storage.SignUploadAsync(reservation, now.AddSeconds(options.PutUrlSeconds), ct);
        return new(reservation.Id, signed.Url, signed.ExpiresAtUtc, new Dictionary<string, string>
        {
            ["Content-Type"] = reservation.ContentType,
            ["Content-Length"] = reservation.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["x-amz-meta-upload-id"] = reservation.Id.ToString("N")
        });
    }

    public Task<TradeImageResponse> ConfirmAsync(Guid user, Guid trade, Guid upload, CancellationToken ct) =>
        repository.ConfirmAsync(user, trade, upload, async reservation =>
        {
            var content = await storage.ReadUploadAsync(reservation.StagingKey, ct);
            if (content is null) throw new ImageConflictException("Le fichier n'a pas été téléversé.");
            if (content.Bytes.LongLength != reservation.SizeBytes || content.ContentType != reservation.ContentType
                || content.UploadId != reservation.Id.ToString("N"))
                throw new ValidationException("Taille ou métadonnées du fichier non conformes.");
            ImageSignatureValidator.Validate(content.Bytes, reservation.ContentType);
            // Upload the exact validated bytes, never copy a mutable staging object after checking it.
            await storage.WriteValidatedAsync(reservation.FinalKey, content.Bytes, reservation.ContentType, ct);
        }, options.MaxImagesPerTrade, ct);

    public Task<IReadOnlyList<TradeImageResponse>> ListAsync(Guid user, Guid trade, CancellationToken ct) => repository.ListAsync(user, trade, ct);
    public async Task<SignedImageUrl> DownloadAsync(Guid user, Guid trade, Guid image, CancellationToken ct)
    {
        var stored = await repository.GetAsync(user, trade, image, ct);
        return await storage.SignDownloadAsync(stored.Key, stored.Image.ContentType,
            time.GetUtcNow().UtcDateTime.AddSeconds(options.GetUrlSeconds), ct);
    }
    public Task DeleteAsync(Guid user, Guid trade, Guid image, CancellationToken ct) => repository.DeleteAsync(user, trade, image, ct);
}
