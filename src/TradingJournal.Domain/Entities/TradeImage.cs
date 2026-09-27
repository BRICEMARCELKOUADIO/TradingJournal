using TradingJournal.Domain.Enums;

namespace TradingJournal.Domain.Entities;

public sealed class TradeImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TradeId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public ImageType ImageType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Trade Trade { get; set; } = null!;
}
