using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TradingJournal.Domain.Enums;

namespace TradingJournal.Application.Images;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RequestImageUpload : IValidatableObject
{
    [Required, EnumDataType(typeof(ImageType))]
    public ImageType? ImageType { get; set; }
    [Required, StringLength(255)]
    public string FileName { get; set; } = string.Empty;
    [Required]
    public string ContentType { get; set; } = string.Empty;
    [Range(1, ImageOptions.MaxBytes)]
    public long SizeBytes { get; set; }
    [Range(0, 10000)]
    public int DisplayOrder { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!ImageOptions.MimeExtensions.TryGetValue(ContentType, out var extensions))
            yield return new("Formats autorisés : JPEG, PNG et WebP.", new[] { nameof(ContentType) });
        else if (!extensions.Any(extension => FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            yield return new("Extension incompatible avec le type MIME.", new[] { nameof(FileName) });
        if (FileName != FileName.Trim() || FileName.Any(c => char.IsControl(c) || "/\\:<>\"|?*".Contains(c))
            || FileName.StartsWith('.') || FileName.EndsWith('.'))
            yield return new("Nom de fichier invalide : aucun chemin n'est autorisé.", new[] { nameof(FileName) });
    }
}

public sealed class ImageOptions
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static readonly IReadOnlyDictionary<string, string[]> MimeExtensions = new Dictionary<string, string[]>
    {
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" }, ["image/png"] = new[] { ".png" }, ["image/webp"] = new[] { ".webp" }
    };
    public int MaxImagesPerTrade { get; set; } = 10;
    public int PutUrlSeconds { get; set; } = 300;
    public int GetUrlSeconds { get; set; } = 120;
    public int UploadLifetimeMinutes { get; set; } = 15;
}

public sealed record ImageUploadReservation(Guid Id, Guid TradeId, string StagingKey, string FinalKey,
    ImageType ImageType, string FileName, string ContentType, long SizeBytes, int DisplayOrder, DateTime ExpiresAtUtc);
public sealed record TradeImageResponse(Guid Id, ImageType ImageType, string FileName, string ContentType, int DisplayOrder, DateTime CreatedAt);
public sealed record StoredTradeImage(TradeImageResponse Image, string Key);
public sealed record UploadedImage(byte[] Bytes, string ContentType, string UploadId);
public sealed record SignedImageUrl(string Url, DateTime ExpiresAtUtc)
{
    public override string ToString() => "[redacted signed URL]";
}
public sealed record UploadImageResponse(Guid UploadId, string UploadUrl, DateTime ExpiresAtUtc, IReadOnlyDictionary<string, string> RequiredHeaders)
{
    public override string ToString() => $"Upload {UploadId} [redacted signed URL]";
}
public sealed class ImageNotFoundException : Exception;
public sealed class ImageConflictException(string message) : Exception(message);
public sealed class ImageStorageUnavailableException : Exception
{
    public ImageStorageUnavailableException() : base("Stockage temporairement indisponible.") { }
}
