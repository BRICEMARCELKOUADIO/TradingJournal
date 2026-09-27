using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TradingJournal.Application.Auth;
using TradingJournal.Infrastructure.Auth;

namespace TradingJournal.Api.Auth;

public static class AuthenticationConfiguration
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, configured) =>
            {
                var jwt = configured.Value;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwt.SigningKey)),
                    RequireSignedTokens = true, RequireExpirationTime = true,
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ValidTypes = new[] { "JWT" }, NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) || id == Guid.Empty)
                            context.Fail("Invalid subject.");
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
        services.AddOptions<AuthRateLimitOptions>().Bind(configuration.GetSection("AuthRateLimiting"))
            .Validate(x => x.LoginPermitLimit is >= 1 and <= 100 && x.RegisterPermitLimit is >= 1 and <= 100
                && x.WindowSeconds is >= 1 and <= 3600, "Invalid authentication rate limits.")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            foreach (var policy in new[] { "login", "register" })
            {
                options.AddPolicy(policy, context =>
                {
                    var settings = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                    // Never trust an unconfigured X-Forwarded-For header.
                    var ip = context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = policy == "login" ? settings.LoginPermitLimit : settings.RegisterPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds), QueueLimit = 0, AutoReplenishment = true
                    });
                });
            }
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Trop de tentatives. Réessayez plus tard."
                }, cancellationToken);
            };
        });
        return services;
    }
}
