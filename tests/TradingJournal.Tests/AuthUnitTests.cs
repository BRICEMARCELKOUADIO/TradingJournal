using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TradingJournal.Api.Auth;
using TradingJournal.Application.Auth;
using TradingJournal.Infrastructure.Auth;
using Xunit;

namespace TradingJournal.Tests;

public sealed class AuthUnitTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-base64-secret")]
    [InlineData("dG9vLXNob3J0")]
    public async Task ApiRefusesToStartWithoutValidSigningKey(string key)
    {
        await using var parent = new AuthApiFactory();
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key })));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ApplicationNormalizesRegistersRejectsDuplicatesAndAuthenticates()
    {
        var users = new MemoryUserRepository();
        var service = new AuthService(users, new Pbkdf2PasswordHasher(), new FakeTokenService(), TimeProvider.System);
        var request = AuthEndpointTests.Registration(" PERSON@EXAMPLE.TEST ");
        var registered = await service.RegisterAsync(request);
        Assert.Equal("person@example.test", registered!.Email);
        Assert.Null(await service.RegisterAsync(request));
        Assert.Single(users.All);
        Assert.Null(await service.LoginAsync(new LoginRequest { Email = request.Email, Password = "wrong" }));
        var authenticated = await service.LoginAsync(new LoginRequest { Email = " PERSON@example.test ", Password = request.Password });
        Assert.Equal(registered, authenticated!.User);
        Assert.Equal("test", authenticated.AccessToken);
        Assert.NotNull(Assert.Single(users.All).LastLoginAt);
    }

    [Fact]
    public void PasswordHashUsesRandomSaltAndVerifiesWithoutPlaintextStorage()
    {
        var hasher = new Pbkdf2PasswordHasher();
        const string password = "Long password 123!";
        var first = hasher.Hash(password);
        var second = hasher.Hash(password);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(password, first);
        Assert.StartsWith("pbkdf2-sha256-v1$600000$", first);
        Assert.True(hasher.Verify(password, first));
        Assert.False(hasher.Verify("Wrong password 123!", first));
        Assert.False(hasher.Verify(password.ToUpperInvariant(), first));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("pbkdf2-sha256-v1$600000$invalid$invalid")]
    [InlineData("pbkdf2-sha256-v1$1$AA==$AA==")]
    public void MissingAndMalformedHashesFailClosed(string? hash) =>
        Assert.False(new Pbkdf2PasswordHasher().Verify("Long password 123!", hash));

    [Fact]
    public async Task ApplicationRejectsInvalidRegistrationWithoutPersistingUser()
    {
        var users = new MemoryUserRepository();
        var service = new AuthService(users, new Pbkdf2PasswordHasher(), new FakeTokenService(), TimeProvider.System);
        await Assert.ThrowsAsync<ValidationException>(() => service.RegisterAsync(new RegisterRequest
        {
            DisplayName = "Name", Email = "person@example.test", Password = "short"
        }));
        Assert.Empty(users.All);
    }

    [Fact]
    public void CurrentUserRequiresAuthenticatedGuidSubject()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var current = new HttpCurrentUser(accessor);
        Assert.Null(current.UserId);
        var id = Guid.NewGuid();
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", id.ToString()) }));
        Assert.Null(current.UserId);
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", id.ToString()) }, "Bearer"));
        Assert.Equal(id, current.UserId);
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "invalid") }, "Bearer"));
        Assert.Null(current.UserId);
    }

    [Fact]
    public void JwtConfigurationRequiresStrongKeyIssuerAudienceAndBoundedLifetime()
    {
        var options = new JwtOptions { Issuer = "issuer", Audience = "audience" };
        Assert.False(options.IsValid());
        options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        Assert.False(options.IsValid());
        options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Assert.True(options.IsValid());
        options.LifetimeMinutes = 0;
        Assert.False(options.IsValid());
        options.LifetimeMinutes = 61;
        Assert.False(options.IsValid());
        options.LifetimeMinutes = 15;
        options.Audience = "";
        Assert.False(options.IsValid());
        options.Audience = "audience";
        options.Issuer = "";
        Assert.False(options.IsValid());
    }

    private sealed class FakeTokenService : IAccessTokenService
    {
        public AccessToken Create(Guid userId) => new("test", DateTime.UtcNow.AddMinutes(15));
    }
}
