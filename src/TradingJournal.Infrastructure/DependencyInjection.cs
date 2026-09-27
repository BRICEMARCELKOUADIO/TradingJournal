using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingJournal.Infrastructure.Persistence;
using TradingJournal.Application.Auth;
using TradingJournal.Infrastructure.Auth;
using TradingJournal.Application.Images;
using TradingJournal.Infrastructure.Images;
using Microsoft.Extensions.Options;

namespace TradingJournal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TradingJournal");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The connection string 'TradingJournal' must be configured.");
        }

        services.AddDbContext<TradingJournalDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddOptions<ImageOptions>().Bind(configuration.GetSection("TradeImages"))
            .Validate(x => x.MaxImagesPerTrade is >= 1 and <= 100 && x.PutUrlSeconds is >= 30 and <= 600
                && x.GetUrlSeconds is >= 30 and <= 300 && x.UploadLifetimeMinutes is >= 15 and <= 60,
                "Invalid TradeImages limits.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ImageOptions>>().Value);
        services.AddOptions<S3ImageOptions>().Bind(configuration.GetSection("TradeImages:S3"))
            .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.BucketName) && !string.IsNullOrWhiteSpace(x.Region)),
                "TradeImages:S3 requires BucketName and Region when enabled.").ValidateOnStart();
        services.AddSingleton<ITradeImageStorage, S3TradeImageStorage>();
        services.AddScoped<ITradeImageRepository, TradeImageRepository>();
        services.AddScoped<ImageCleanup>();
        services.AddScoped<TradingJournal.Application.Trades.ITradeRepository, TradeRepository>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IAccessTokenService, JwtTokenService>();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => options.IsValid(),
                "Jwt requires Issuer, Audience, a Base64 SigningKey containing 32–64 random bytes, and LifetimeMinutes between 1 and 60.")
            .ValidateOnStart();
        return services;
    }
}
