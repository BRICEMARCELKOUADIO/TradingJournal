using TradingJournal.Domain.Enums;

namespace TradingJournal.Infrastructure.Images;

// Technical durable reservation; deliberately survives trade deletion until cleanup.
public sealed class ImageUpload
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TradeId { get; set; }
    public string StagingKey { get; set; } = string.Empty;
    public string FinalKey { get; set; } = string.Empty;
    public ImageType ImageType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class ImageDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StorageKey { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
}
