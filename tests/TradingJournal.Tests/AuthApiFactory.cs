using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TradingJournal.Application.Auth;
using TradingJournal.Application.Images;
using TradingJournal.Domain.Entities;
using TradingJournal.Infrastructure.Persistence;

namespace TradingJournal.Tests;

public sealed class AuthApiFactory(string? sqlConnection = null, int loginLimit = 5, int registerLimit = 5,
    ITradeImageStorage? imageStorage = null, int imageLimit = 10)
    : WebApplicationFactory<Program>
{
    public const string Issuer = "TradingJournal.Tests";
    public const string Audience = "TradingJournal.Tests.Client";
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public MemoryUserRepository Users { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience, ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:LifetimeMinutes"] = "15",
            ["AuthRateLimiting:LoginPermitLimit"] = loginLimit.ToString(),
            ["AuthRateLimiting:RegisterPermitLimit"] = registerLimit.ToString(),
            ["AuthRateLimiting:WindowSeconds"] = "60",
            ["TradeImages:S3:Enabled"] = "false", ["TradeImages:MaxImagesPerTrade"] = imageLimit.ToString()
        }));
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            if (imageStorage is not null)
            {
                services.RemoveAll<ITradeImageStorage>();
                services.AddSingleton(imageStorage);
            }
            if (sqlConnection is null)
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(Users);
            }
            else
            {
                services.RemoveAll<TradingJournalDbContext>();
                services.AddScoped(_ => new TradingJournalDbContext(new DbContextOptionsBuilder<TradingJournalDbContext>()
                    .UseSqlServer(sqlConnection).Options));
            }
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });
}

public sealed class MemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, User> users = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<User> All => users.Values.ToArray();
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(users.GetValueOrDefault(email));
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(users.Values.SingleOrDefault(u => u.Id == id));
    public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken) => Task.FromResult(users.TryAdd(user.Email, user));
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
