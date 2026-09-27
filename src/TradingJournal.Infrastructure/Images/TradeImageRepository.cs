using Microsoft.EntityFrameworkCore;
using TradingJournal.Application.Images;
using TradingJournal.Domain.Entities;
using TradingJournal.Infrastructure.Persistence;

namespace TradingJournal.Infrastructure.Images;

public sealed class TradeImageRepository(TradingJournalDbContext db, TimeProvider time) : ITradeImageRepository
{
    private IQueryable<TradeImage> Owned(Guid user, Guid trade) => db.TradeImages.Where(i => i.TradeId == trade && i.Trade.UserId == user);
    private async Task RequireTrade(Guid user, Guid trade, CancellationToken ct)
    {
        if (!await db.Trades.AnyAsync(t => t.Id == trade && t.UserId == user, ct)) throw new ImageNotFoundException();
    }
    private async Task LockTrade(Guid user, Guid trade, CancellationToken ct)
    {
        if (await db.Trades.FromSqlInterpolated($"SELECT * FROM [Trades] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {trade} AND [UserId] = {user}")
            .AsNoTracking().SingleOrDefaultAsync(ct) is null) throw new ImageNotFoundException();
    }
    private static ImageUploadReservation Reservation(ImageUpload u) => new(u.Id, u.TradeId, u.StagingKey, u.FinalKey,
        u.ImageType, u.FileName, u.ContentType, u.SizeBytes, u.DisplayOrder, DateTime.SpecifyKind(u.ExpiresAtUtc, DateTimeKind.Utc));
    private static TradeImageResponse Response(TradeImage i) => new(i.Id, i.ImageType, i.FileName, i.ContentType,
        i.DisplayOrder, DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc));

    public async Task<ImageUploadReservation> ReserveAsync(Guid user, Guid trade, RequestImageUpload request, DateTime expires, int maximum, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTrade(user, trade, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var count = await Owned(user, trade).CountAsync(ct) + await db.Set<ImageUpload>()
            .CountAsync(u => u.UserId == user && u.TradeId == trade && !u.Confirmed && u.ExpiresAtUtc > now, ct);
        if (count >= maximum) throw new ImageConflictException("Limite d'images ou de demandes en attente atteinte.");
        var upload = new ImageUpload
        {
            Id = Guid.NewGuid(), UserId = user, TradeId = trade,
            StagingKey = $"trade-images/staging/{Guid.NewGuid():N}", FinalKey = $"trade-images/confirmed/{Guid.NewGuid():N}",
            ImageType = request.ImageType!.Value, FileName = request.FileName, ContentType = request.ContentType,
            SizeBytes = request.SizeBytes, DisplayOrder = request.DisplayOrder, ExpiresAtUtc = expires
        };
        db.Add(upload);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Reservation(upload);
    }

    public async Task<TradeImageResponse> ConfirmAsync(Guid user, Guid trade, Guid uploadId,
        Func<ImageUploadReservation, Task> validateAndStore, int maximum, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTrade(user, trade, ct);
        // Idempotence also survives reservation cleanup; image ID is the upload ID.
        var existing = await Owned(user, trade).AsNoTracking().SingleOrDefaultAsync(i => i.Id == uploadId, ct);
        if (existing is not null) return Response(existing);
        var upload = await db.Set<ImageUpload>().FromSqlInterpolated(
            $"SELECT * FROM [ImageUploads] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {uploadId} AND [UserId] = {user} AND [TradeId] = {trade}")
            .SingleOrDefaultAsync(ct) ?? throw new ImageNotFoundException();
        if (upload.Confirmed) throw new ImageNotFoundException();
        if (upload.ExpiresAtUtc <= time.GetUtcNow().UtcDateTime) throw new ImageConflictException("Demande de téléversement expirée.");
        if (await Owned(user, trade).CountAsync(ct) >= maximum) throw new ImageConflictException("Limite d'images atteinte.");
        await validateAndStore(Reservation(upload));
        var image = new TradeImage
        {
            Id = upload.Id, TradeId = trade, StorageKey = upload.FinalKey, ImageType = upload.ImageType,
            FileName = upload.FileName, ContentType = upload.ContentType, DisplayOrder = upload.DisplayOrder,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        db.Add(image);
        upload.Confirmed = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Response(image);
    }

    public async Task<IReadOnlyList<TradeImageResponse>> ListAsync(Guid user, Guid trade, CancellationToken ct)
    {
        await RequireTrade(user, trade, ct);
        var images = await Owned(user, trade).AsNoTracking().OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(ct);
        return images.Select(Response).ToArray();
    }
    public async Task<StoredTradeImage> GetAsync(Guid user, Guid trade, Guid imageId, CancellationToken ct)
    {
        var image = await Owned(user, trade).AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, ct) ?? throw new ImageNotFoundException();
        return new(Response(image), image.StorageKey);
    }
    public async Task DeleteAsync(Guid user, Guid trade, Guid imageId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTrade(user, trade, ct);
        var image = await Owned(user, trade).AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, ct) ?? throw new ImageNotFoundException();
        db.Add(new ImageDeletion { StorageKey = image.StorageKey, NextAttemptAtUtc = time.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
        await Owned(user, trade).Where(i => i.Id == imageId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }
}
