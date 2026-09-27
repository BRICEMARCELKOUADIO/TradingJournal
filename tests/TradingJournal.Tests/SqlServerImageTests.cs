using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TradingJournal.Application.Auth;
using TradingJournal.Application.Images;
using TradingJournal.Domain.Entities;
using TradingJournal.Domain.Enums;
using TradingJournal.Infrastructure.Images;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class SqlServerImageTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private async Task<(HttpClient Client, Guid Trade)> Setup(AuthApiFactory factory)
    {
        var user = new User { DisplayName = "Image trader", Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = "test-only" };
        var trade = new Trade { User = user, Symbol = "EURUSD", EntryDateTime = DateTime.UtcNow, PositionSize = 1, EntryPrice = 1 };
        await using var db = fixture.CreateContext();
        db.Add(trade);
        await db.SaveChangesAsync();
        var client = factory.CreateHttpsClient();
        using var scope = factory.Services.CreateScope();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Create(user.Id).Value);
        return (client, trade.Id);
    }
    private static string Root(Guid trade) => $"/api/trades/{trade}/images";
    private static RequestImageUpload Request(ImageType type = ImageType.Before) => new()
    {
        FileName = "chart.png", ContentType = "image/png", SizeBytes = TestImageBytes.Png.Length, ImageType = type
    };
    private static async Task<UploadImageResponse> Reserve(HttpClient client, Guid trade, RequestImageUpload? request = null)
    {
        var response = await client.PostAsJsonAsync(Root(trade) + "/uploads", request ?? Request());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UploadImageResponse>())!;
    }
    private static Task<HttpResponseMessage> Confirm(HttpClient client, Guid trade, Guid upload) =>
        client.PostAsync(Root(trade) + $"/uploads/{upload}/confirm", null);
    private async Task ExpireAndClean(FakeTradeImageStorage storage)
    {
        await using var db = fixture.CreateContext();
        await db.Set<ImageUpload>().ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await db.Set<ImageDeletion>().ExecuteUpdateAsync(s => s.SetProperty(u => u.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await new ImageCleanup(db, storage, TimeProvider.System).RunOnceAsync(default);
    }

    [SqlServerFact]
    public async Task UploadConfirmListDownloadAndDeleteArePrivateAndIdempotent()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        var before = await Reserve(client, session.Trade);
        var after = await Reserve(client, session.Trade, Request(ImageType.After));
        Assert.Empty((await client.GetFromJsonAsync<TradeImageResponse[]>(Root(session.Trade)))!);
        foreach (var upload in new[] { before, after })
        {
            storage.Put(upload.UploadId);
            Assert.Equal(HttpStatusCode.OK, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        }
        storage.Put(before.UploadId, "malicious replacement"u8.ToArray());
        Assert.Equal(HttpStatusCode.OK, (await Confirm(client, session.Trade, before.UploadId)).StatusCode);
        Assert.Equal(2, storage.Writes);
        Assert.Equal(TestImageBytes.Png, storage.Objects[storage.Uploads[before.UploadId].FinalKey].Bytes);
        var listResponse = await client.GetAsync(Root(session.Trade));
        var list = (await listResponse.Content.ReadFromJsonAsync<TradeImageResponse[]>())!;
        Assert.Equal(2, list.Length);
        Assert.Contains(list, x => x.ImageType == ImageType.Before);
        Assert.Contains(list, x => x.ImageType == ImageType.After);
        Assert.DoesNotContain("storageKey", await listResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var download = await client.GetAsync(Root(session.Trade) + $"/{before.UploadId}/download-url");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True(download.Headers.CacheControl!.NoStore);
        await ExpireAndClean(storage);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(client, session.Trade, before.UploadId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Root(session.Trade) + $"/{before.UploadId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(session.Trade) + $"/{before.UploadId}/download-url")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirm(client, session.Trade, before.UploadId)).StatusCode);
        await ExpireAndClean(storage);
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[before.UploadId].FinalKey));
        Assert.True(storage.Objects.ContainsKey(storage.Uploads[after.UploadId].FinalKey));
    }

    [SqlServerFact]
    public async Task AbsentWrongSizeMimeMetadataAndSignatureCannotBeConfirmed()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        var upload = await Reserve(client, session.Trade);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        var invalidSignature = TestImageBytes.Png;
        invalidSignature[0] = 0;
        foreach (var bad in new[]
        {
            new UploadedImage(Array.Empty<byte>(), "image/png", upload.UploadId.ToString("N")),
            new UploadedImage(TestImageBytes.Png, "image/jpeg", upload.UploadId.ToString("N")),
            new UploadedImage(TestImageBytes.Png, "image/png", "wrong"),
            new UploadedImage(invalidSignature, "image/png", upload.UploadId.ToString("N"))
        })
        {
            storage.Objects[storage.Uploads[upload.UploadId].StagingKey] = bad;
            Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        }
        Assert.Equal(0, storage.Writes);
        Assert.Empty((await client.GetFromJsonAsync<TradeImageResponse[]>(Root(session.Trade)))!);
    }

    [SqlServerFact]
    public async Task ConcurrentReservationsEnforceLimitAndConfirmationsDoNotDuplicate()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage, imageLimit: 1);
        var session = await Setup(factory);
        using var client = session.Client;
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Root(session.Trade) + "/uploads", Request()),
            client.PostAsJsonAsync(Root(session.Trade) + "/uploads", Request()));
        var accepted = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var upload = (await accepted.Content.ReadFromJsonAsync<UploadImageResponse>())!;
        storage.Put(upload.UploadId);
        var confirmations = await Task.WhenAll(Confirm(client, session.Trade, upload.UploadId), Confirm(client, session.Trade, upload.UploadId));
        Assert.All(confirmations, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, storage.Writes);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root(session.Trade) + "/uploads", Request())).StatusCode);
    }

    [SqlServerFact]
    public async Task AllOperationsEnforceTradeAndImageOwnership()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var owner = await Setup(factory);
        var other = await Setup(factory);
        using var client = owner.Client;
        using var attacker = other.Client;
        var upload = await Reserve(client, owner.Trade);
        storage.Put(upload.UploadId);
        await Confirm(client, owner.Trade, upload.UploadId);
        foreach (var trade in new[] { owner.Trade, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsJsonAsync(Root(trade) + "/uploads", Request())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync(Root(trade))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Confirm(attacker, trade, upload.UploadId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync(Root(trade) + $"/{upload.UploadId}/download-url")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await attacker.DeleteAsync(Root(trade) + $"/{upload.UploadId}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync(Root(other.Trade) + $"/{upload.UploadId}/download-url")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Confirm(attacker, other.Trade, upload.UploadId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root(owner.Trade) + "/uploads",
            new { userId = Guid.NewGuid(), imageType = 0, fileName = "chart.png", contentType = "image/png", sizeBytes = 50 })).StatusCode);
    }

    [SqlServerFact]
    public async Task StorageFailuresAndExpiredReservationsAreRecoverableWithoutExposingObjects()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        storage.FailSign = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Root(session.Trade) + "/uploads", Request())).StatusCode);
        storage.FailSign = false;
        var upload = await Reserve(client, session.Trade);
        storage.Put(upload.UploadId);
        storage.FailRead = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        storage.FailRead = false;
        storage.FailWriteAfterStoring = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<TradeImageResponse[]>(Root(session.Trade)))!);
        Assert.True(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
        await using (var db = fixture.CreateContext())
            await db.Set<ImageUpload>().Where(u => u.Id == upload.UploadId).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        await ExpireAndClean(storage);
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].StagingKey));
    }

    [SqlServerFact]
    public async Task DeleteQueueSurvivesS3FailureAndTradeDeletionAlsoQueuesObjects()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        var upload = await Reserve(client, session.Trade);
        storage.Put(upload.UploadId);
        await Confirm(client, session.Trade, upload.UploadId);
        storage.FailDelete = true;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Root(session.Trade) + $"/{upload.UploadId}")).StatusCode);
        await ExpireAndClean(storage);
        await using (var db = fixture.CreateContext())
            Assert.True(await db.Set<ImageDeletion>().AnyAsync(d => d.StorageKey == storage.Uploads[upload.UploadId].FinalKey && d.Attempts > 0));
        storage.FailDelete = false;
        await ExpireAndClean(storage);
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
        var second = await Reserve(client, session.Trade);
        storage.Put(second.UploadId);
        await Confirm(client, session.Trade, second.UploadId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{session.Trade}")).StatusCode);
        await ExpireAndClean(storage);
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[second.UploadId].FinalKey));
    }

    [SqlServerFact]
    public async Task SqlFailureAfterS3WriteLeavesDurableReservationForOrphanCleanup()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        var upload = await Reserve(client, session.Trade);
        storage.Put(upload.UploadId);
        await using var db = new TradingJournalDbContext(new DbContextOptionsBuilder<TradingJournalDbContext>()
            .UseSqlServer(fixture.ConnectionString).AddInterceptors(new FailImageSave()).Options);
        var owner = await db.Trades.Where(t => t.Id == session.Trade).Select(t => t.UserId).SingleAsync();
        var service = new TradeImageService(new TradeImageRepository(db, TimeProvider.System), storage, new ImageOptions(), TimeProvider.System);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ConfirmAsync(owner, session.Trade, upload.UploadId, default));
        Assert.True(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
        Assert.Empty((await client.GetFromJsonAsync<TradeImageResponse[]>(Root(session.Trade)))!);
        await ExpireAndClean(storage);
        Assert.False(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
    }

    [SqlServerFact]
    public async Task ConfirmationCanRetryAfterTransientStorageFailure()
    {
        var storage = new FakeTradeImageStorage();
        await using var factory = new AuthApiFactory(fixture.ConnectionString, imageStorage: storage);
        var session = await Setup(factory);
        using var client = session.Client;
        var upload = await Reserve(client, session.Trade);
        storage.Put(upload.UploadId);
        storage.FailWriteAfterStoring = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        storage.FailWriteAfterStoring = false;
        Assert.Equal(HttpStatusCode.OK, (await Confirm(client, session.Trade, upload.UploadId)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<TradeImageResponse[]>(Root(session.Trade)))!);
        await ExpireAndClean(storage);
        Assert.True(storage.Objects.ContainsKey(storage.Uploads[upload.UploadId].FinalKey));
    }

    private sealed class FailImageSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TradeImage>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("Simulated SQL failure.");
            return ValueTask.FromResult(result);
        }
    }
}
