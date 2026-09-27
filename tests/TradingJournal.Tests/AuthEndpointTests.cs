using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using TradingJournal.Application.Auth;
using TradingJournal.Infrastructure.Auth;
using Xunit;

namespace TradingJournal.Tests;

public sealed class AuthEndpointTests
{
    public const string Password = "A strong password 123!";
    public static RegisterRequest Registration(string email = "alice@example.test") => new()
    {
        DisplayName = " Alice ", Email = email, Password = Password
    };

    [Fact]
    public async Task RegisterLoginAndMeReturnOnlyAllowedData()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        using var registration = await client.PostAsJsonAsync("/api/auth/register",
            new { displayName = " Alice ", email = " ALICE@Example.Test ", password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registered = await registration.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("alice@example.test", registered!.Email);
        Assert.Equal("Alice", registered.DisplayName);
        Assert.True(registration.Headers.CacheControl!.NoStore);
        var saved = Assert.Single(factory.Users.All);
        Assert.True(new Pbkdf2PasswordHasher().Verify(Password, saved.PasswordHash));
        Assert.False(saved.EmailVerified);
        Assert.Null(saved.LastLoginAt);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "ALICE@EXAMPLE.TEST", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl!.NoStore);
        var authenticated = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(saved.LastLoginAt);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(authenticated!.AccessToken);
        Assert.Equal(saved.Id.ToString(), token.Subject);
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal(AuthApiFactory.Issuer, token.Issuer);
        Assert.Equal(AuthApiFactory.Audience, Assert.Single(token.Audiences));
        Assert.InRange(authenticated.ExpiresAtUtc - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticated.AccessToken);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(registered, await me.Content.ReadFromJsonAsync<UserResponse>());
        foreach (var response in new[] { registration, me })
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "displayName", "email", "id" }, json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        }
        Assert.DoesNotContain("password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateNormalizedEmailIsRejected()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", Registration())).StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", Registration(" ALICE@EXAMPLE.TEST "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(factory.Users.All);
    }

    [Theory]
    [InlineData("", "alice@example.test", "A strong password 123!")]
    [InlineData("   ", "alice@example.test", "A strong password 123!")]
    [InlineData("Alice", "invalid", "A strong password 123!")]
    [InlineData("Alice", "", "A strong password 123!")]
    [InlineData("Alice", "alice@example.test", "12345678901")]
    [InlineData("Alice", "alice@example.test", null)]
    public async Task InvalidRegistrationIsRejected(string name, string email, string? password)
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { displayName = name, email, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Users.All);
    }

    [Fact]
    public async Task ExcessiveLengthsAreRejectedAndTwelveCharacterPasswordIsAccepted()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        foreach (var input in new[]
        {
            new { displayName = new string('a', 101), email = "a@example.test", password = Password },
            new { displayName = "Alice", email = new string('a', 250) + "@example.test", password = Password },
            new { displayName = "Alice", email = "a@example.test", password = new string('a', 129) }
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", input)).StatusCode);
        Assert.Empty(factory.Users.All);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register",
            new { displayName = "Alice", email = "a@example.test", password = "123456789012" })).StatusCode);
    }

    [Fact]
    public async Task InvalidCredentialsHaveSameGenericErrorAndDoNotUpdateLastLogin()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/auth/register", Registration());
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "missing@example.test", password = Password });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "alice@example.test", password = "Wrong password!" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var first = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        using var second = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync());
        Assert.Equal(first.RootElement.GetProperty("title").GetString(), second.RootElement.GetProperty("title").GetString());
        Assert.Null(Assert.Single(factory.Users.All).LastLoginAt);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("future")]
    [InlineData("subject")]
    [InlineData("algorithm")]
    [InlineData("malformed")]
    [InlineData("noexpiry")]
    [InlineData("unsigned")]
    public async Task MeRejectsInvalidTokens(string kind)
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateHttpsClient();
        await client.PostAsJsonAsync("/api/auth/register", Registration());
        var id = Assert.Single(factory.Users.All).Id;
        if (kind != "absent")
        {
            var key = kind == "signature" ? RandomNumberGenerator.GetBytes(64) : Convert.FromBase64String(factory.SigningKey);
            var token = new JwtSecurityToken(
                kind == "issuer" ? "untrusted" : AuthApiFactory.Issuer,
                kind == "audience" ? "wrong" : AuthApiFactory.Audience,
                new[] { new Claim("sub", kind == "subject" ? "invalid" : id.ToString()) },
                DateTime.UtcNow.AddMinutes(kind == "future" ? 1 : -10),
                kind == "noexpiry" ? null : DateTime.UtcNow.AddMinutes(kind == "expired" ? -1 : 5),
                kind == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(key),
                    kind == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                kind == "malformed" ? "invalid" : new JwtSecurityTokenHandler().WriteToken(token));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RepeatedLoginAttemptsAreLimitedAndForwardedHeaderDoesNotBypassLimit()
    {
        await using var factory = new AuthApiFactory(loginLimit: 2);
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
                new { email = $"unknown{i}@example.test", password = Password })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.100");
        var blocked = await client.PostAsJsonAsync("/api/auth/login", new { email = "another@example.test", password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    [Fact]
    public async Task RepeatedRegistrationsAreLimited()
    {
        await using var factory = new AuthApiFactory(registerLimit: 1);
        using var client = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", Registration())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/register", Registration("second@example.test"))).StatusCode);
        Assert.Single(factory.Users.All);
    }
}
