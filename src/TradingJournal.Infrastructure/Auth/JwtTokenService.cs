using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TradingJournal.Application.Auth;

namespace TradingJournal.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenService
{
    public AccessToken Create(Guid userId)
    {
        var settings = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
            }, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
