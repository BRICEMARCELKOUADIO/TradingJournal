using System.Collections.Concurrent;
using TradingJournal.Application.Images;

namespace TradingJournal.Tests;

public sealed class FakeTradeImageStorage : ITradeImageStorage
{
    public ConcurrentDictionary<Guid, ImageUploadReservation> Uploads { get; } = new();
    public ConcurrentDictionary<string, UploadedImage> Objects { get; } = new();
    public bool FailRead { get; set; }
    public bool FailWriteAfterStoring { get; set; }
    public bool FailDelete { get; set; }
    public bool FailSign { get; set; }
    public int Writes;
    public Task<SignedImageUrl> SignUploadAsync(ImageUploadReservation upload, DateTime expires, CancellationToken ct)
    {
        Uploads[upload.Id] = upload;
        if (FailSign) throw new ImageStorageUnavailableException();
        return Task.FromResult(new SignedImageUrl($"https://storage.invalid/upload/{upload.Id}", expires));
    }
    public void Put(Guid uploadId, byte[]? bytes = null, string? mime = null, string? metadata = null)
    {
        var upload = Uploads[uploadId];
        Objects[upload.StagingKey] = new(bytes ?? TestImageBytes.Png, mime ?? upload.ContentType, metadata ?? upload.Id.ToString("N"));
    }
    public Task<UploadedImage?> ReadUploadAsync(string key, CancellationToken ct)
    {
        if (FailRead) throw new ImageStorageUnavailableException();
        var obj = Objects.GetValueOrDefault(key);
        return Task.FromResult(obj is null ? null : obj with { Bytes = obj.Bytes.ToArray() });
    }
    public Task WriteValidatedAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        Objects[key] = new(bytes.ToArray(), contentType, "");
        Interlocked.Increment(ref Writes);
        if (FailWriteAfterStoring) throw new ImageStorageUnavailableException();
        return Task.CompletedTask;
    }
    public Task<SignedImageUrl> SignDownloadAsync(string key, string contentType, DateTime expires, CancellationToken ct)
    {
        if (FailSign) throw new ImageStorageUnavailableException();
        return Task.FromResult(new SignedImageUrl("https://storage.invalid/download/opaque", expires));
    }
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        if (FailDelete) throw new ImageStorageUnavailableException();
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

