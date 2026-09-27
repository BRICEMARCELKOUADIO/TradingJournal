using System.ComponentModel.DataAnnotations;
using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using TradingJournal.Application.Images;

namespace TradingJournal.Infrastructure.Images;

public sealed class S3TradeImageStorage : ITradeImageStorage, IDisposable
{
    private readonly S3ImageOptions options;
    private readonly Lazy<IAmazonS3> client;

    public S3TradeImageStorage(IOptions<S3ImageOptions> options)
    {
        this.options = options.Value;
        // No credentials supplied: SDK profile/environment/container/instance role chain.
        client = new Lazy<IAmazonS3>(() => new AmazonS3Client(new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(this.options.Region),
            Timeout = TimeSpan.FromSeconds(30), MaxErrorRetry = 1, DisableLogging = true, LogResponse = false, LogMetrics = false
        }));
    }

    public S3TradeImageStorage(IOptions<S3ImageOptions> options, IAmazonS3 client)
    {
        this.options = options.Value;
        this.client = new Lazy<IAmazonS3>(() => client);
    }

    private async Task<T> Execute<T>(Func<IAmazonS3, Task<T>> action)
    {
        if (!options.Enabled) throw new ImageStorageUnavailableException();
        try { return await action(client.Value); }
        catch (Exception error) when (error is AmazonServiceException or AmazonClientException or HttpRequestException or IOException)
        {
            // Do not propagate SDK messages/inner exceptions that could contain signed URLs or credentials.
            throw new ImageStorageUnavailableException();
        }
    }

    public Task<SignedImageUrl> SignUploadAsync(ImageUploadReservation upload, DateTime expiresAt, CancellationToken ct) => Execute(async s3 =>
    {
        ct.ThrowIfCancellationRequested();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName, Key = upload.StagingKey, Verb = HttpVerb.PUT,
            Expires = expiresAt, Protocol = Protocol.HTTPS, ContentType = upload.ContentType
        };
        request.Metadata.Add("upload-id", upload.Id.ToString("N"));
        request.Headers.ContentLength = upload.SizeBytes;
        return new SignedImageUrl(await s3.GetPreSignedURLAsync(request), expiresAt);
    });

    public Task<UploadedImage?> ReadUploadAsync(string key, CancellationToken ct) => Execute<UploadedImage?>(async s3 =>
    {
        GetObjectResponse response;
        try { response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = options.BucketName, Key = key }, ct); }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
        using (response)
        {
            if (response.ContentLength is <= 0 or > ImageOptions.MaxBytes || !string.IsNullOrEmpty(response.Headers.ContentEncoding))
                throw new ValidationException("Taille ou encodage de fichier non conforme.");
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await response.ResponseStream.ReadAsync(buffer, ct)) != 0)
            {
                if (output.Length + count > ImageOptions.MaxBytes) throw new ValidationException("Fichier trop volumineux.");
                output.Write(buffer, 0, count);
            }
            return new UploadedImage(output.ToArray(), response.Headers.ContentType ?? string.Empty,
                response.Metadata["x-amz-meta-upload-id"] ?? string.Empty);
        }
    });

    public async Task WriteValidatedAsync(string key, byte[] bytes, string contentType, CancellationToken ct) =>
        await Execute(async s3 =>
        {
            using var input = new MemoryStream(bytes, writable: false);
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.BucketName, Key = key, InputStream = input, ContentType = contentType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
                // No ACL: BucketOwnerEnforced and Block Public Access are required bucket settings.
            }, ct);
            return true;
        });

    public Task<SignedImageUrl> SignDownloadAsync(string key, string contentType, DateTime expiresAt, CancellationToken ct) => Execute(async s3 =>
    {
        ct.ThrowIfCancellationRequested();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName, Key = key, Verb = HttpVerb.GET, Expires = expiresAt, Protocol = Protocol.HTTPS,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = contentType, ContentDisposition = "attachment", CacheControl = "no-store"
            }
        };
        return new SignedImageUrl(await s3.GetPreSignedURLAsync(request), expiresAt);
    });

    public async Task DeleteAsync(string key, CancellationToken ct) => await Execute(async s3 =>
    {
        await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = options.BucketName, Key = key }, ct);
        return true;
    });

    public void Dispose() { if (client.IsValueCreated) client.Value.Dispose(); }
}
