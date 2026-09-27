using Microsoft.EntityFrameworkCore;
using TradingJournal.Application.Images;
using TradingJournal.Infrastructure.Persistence;

namespace TradingJournal.Infrastructure.Images;

public sealed class ImageCleanup(TradingJournalDbContext db, ITradeImageStorage storage, TimeProvider time)
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var expired = await db.Set<ImageUpload>().FromSqlInterpolated(
                $"SELECT * FROM [ImageUploads] WITH (UPDLOCK, HOLDLOCK) WHERE [ExpiresAtUtc] <= {now}")
                .OrderBy(u => u.ExpiresAtUtc).Take(100).ToListAsync(ct);
            foreach (var upload in expired)
            {
                db.Add(new ImageDeletion { StorageKey = upload.StagingKey, NextAttemptAtUtc = now });
                if (!upload.Confirmed) db.Add(new ImageDeletion { StorageKey = upload.FinalKey, NextAttemptAtUtc = now });
            }
            db.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var pending = await db.Set<ImageDeletion>().AsNoTracking().Where(d => d.NextAttemptAtUtc <= now)
            .OrderBy(d => d.NextAttemptAtUtc).Take(100).ToListAsync(ct);
        foreach (var deletion in pending)
        {
            try
            {
                await storage.DeleteAsync(deletion.StorageKey, ct);
                await db.Set<ImageDeletion>().Where(d => d.Id == deletion.Id).ExecuteDeleteAsync(ct);
            }
            catch (ImageStorageUnavailableException)
            {
                var retry = now.AddSeconds(30 * Math.Pow(2, Math.Min(deletion.Attempts, 6)));
                await db.Set<ImageDeletion>().Where(d => d.Id == deletion.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Attempts, d => d.Attempts + 1).SetProperty(d => d.NextAttemptAtUtc, retry), ct);
            }
        }
    }
}
