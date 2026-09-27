namespace TradingJournal.Infrastructure.Images;

public sealed class S3ImageOptions
{
    public bool Enabled { get; set; }
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool CleanupEnabled { get; set; } = true;
}
