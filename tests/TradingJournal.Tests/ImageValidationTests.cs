using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using TradingJournal.Application.Images;
using TradingJournal.Domain.Enums;
using TradingJournal.Infrastructure.Images;
using Xunit;

namespace TradingJournal.Tests;

public sealed class ImageValidationTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void RealImagesMatchSignaturesAndTruncationOrWrongMimeIsRejected(string mime)
    {
        var bytes = mime switch { "image/png" => TestImageBytes.Png, "image/jpeg" => TestImageBytes.Jpeg, _ => TestImageBytes.Webp };
        ImageSignatureValidator.Validate(bytes, mime);
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(bytes[..^1], mime));
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(bytes.Concat("<script>"u8.ToArray()).ToArray(), mime));
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(bytes, mime == "image/png" ? "image/jpeg" : "image/png"));
    }
    [Fact]
    public void EmptyFakeAndOversizedImagesAreRejected()
    {
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(Array.Empty<byte>(), "image/png"));
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate("<svg onload='alert(1)'/>"u8.ToArray(), "image/png"));
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(new byte[ImageOptions.MaxBytes + 1], "image/png"));
        var corrupt = TestImageBytes.Png;
        corrupt[45] ^= 1;
        Assert.Throws<ValidationException>(() => ImageSignatureValidator.Validate(corrupt, "image/png"));
    }
    [Theory]
    [InlineData("../chart.png", "image/png", 100)]
    [InlineData("C:\\chart.png", "image/png", 100)]
    [InlineData("bad\nname.png", "image/png", 100)]
    [InlineData("chart.svg", "image/svg+xml", 100)]
    [InlineData("chart.jpg", "image/png", 100)]
    [InlineData("chart.png", "image/png", 0)]
    [InlineData("chart.png", "image/png", ImageOptions.MaxBytes + 1)]
    public void UploadRequestRejectsUnsafeNamesTypesAndSizes(string name, string type, long size)
    {
        var errors = new List<ValidationResult>();
        var input = new RequestImageUpload { FileName = name, ContentType = type, SizeBytes = size, ImageType = ImageType.Before };
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
    }

    [Fact]
    public async Task SdkSignsPutHeadersAndPrivateDownloadWithoutCallingAws()
    {
        // Ephemeral, synthetic credentials used only for local signature generation; never real AWS keys.
        using var client = new AmazonS3Client(new BasicAWSCredentials(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), RegionEndpoint.USEast1);
        using var storage = new S3TradeImageStorage(Options.Create(new S3ImageOptions { Enabled = true, BucketName = "test-private-bucket", Region = "us-east-1" }), client);
        var reservation = new ImageUploadReservation(Guid.NewGuid(), Guid.NewGuid(), "trade-images/staging/random",
            "trade-images/confirmed/random", ImageType.Before, "chart.png", "image/png", 100, 0, DateTime.UtcNow.AddMinutes(15));
        var put = await storage.SignUploadAsync(reservation, DateTime.UtcNow.AddMinutes(5), default);
        var query = Uri.UnescapeDataString(new Uri(put.Url).Query);
        Assert.Equal("https", new Uri(put.Url).Scheme);
        Assert.True(query.Contains("content-length", StringComparison.OrdinalIgnoreCase), "Content length must be signed.");
        Assert.True(query.Contains("content-type", StringComparison.OrdinalIgnoreCase), "Content type must be signed.");
        Assert.True(query.Contains("upload-id", StringComparison.OrdinalIgnoreCase), "Upload metadata must be signed.");
        var get = await storage.SignDownloadAsync(reservation.FinalKey, "image/png", DateTime.UtcNow.AddMinutes(2), default);
        Assert.True(Uri.UnescapeDataString(new Uri(get.Url).Query).Contains("response-content-disposition=attachment"),
            "Downloads must use attachment disposition; signed URLs are intentionally excluded from assertion diagnostics.");
        Assert.DoesNotContain(put.Url, put.ToString());
    }

    [Fact]
    public async Task EveryImageEndpointRequiresJwt()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        var root = $"/api/trades/{Guid.NewGuid()}/images";
        var id = Guid.NewGuid();
        var responses = new[]
        {
            await client.PostAsJsonAsync(root + "/uploads", new RequestImageUpload()),
            await client.PostAsync(root + $"/uploads/{id}/confirm", null),
            await client.GetAsync(root), await client.GetAsync(root + $"/{id}/download-url"),
            await client.DeleteAsync(root + $"/{id}")
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public void ImageTypeIsRequiredAndMustBeDefined()
    {
        var input = new RequestImageUpload { FileName = "chart.png", ContentType = "image/png", SizeBytes = 100 };
        Assert.NotEmpty(TradeValidationTests.Errors(input));
        input.ImageType = (ImageType)99;
        Assert.NotEmpty(TradeValidationTests.Errors(input));
    }
}
