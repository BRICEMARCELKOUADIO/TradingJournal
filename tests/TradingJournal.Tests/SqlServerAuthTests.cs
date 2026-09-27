using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Application.Auth;
using TradingJournal.Infrastructure.Auth;
using Xunit;

namespace TradingJournal.Tests;

public sealed class SqlServerAuthTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task RealDatabaseSupportsRegistrationLoginAndAuthenticatedProfile()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var client = factory.CreateHttpsClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { displayName = " Alice ", email = $" {email.ToUpperInvariant()} ", password = AuthEndpointTests.Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registered = await response.Content.ReadFromJsonAsync<UserResponse>();
        await using var context = fixture.CreateContext();
        var stored = await context.Users.AsNoTracking().SingleAsync(u => u.Id == registered!.Id);
        Assert.Equal(email, stored.Email);
        Assert.NotEqual(AuthEndpointTests.Password, stored.PasswordHash);
        Assert.True(new Pbkdf2PasswordHasher().Verify(AuthEndpointTests.Password, stored.PasswordHash));
        Assert.Null(stored.LastLoginAt);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = AuthEndpointTests.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authenticated = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticated!.AccessToken);
        Assert.Equal(registered, await client.GetFromJsonAsync<UserResponse>("/api/auth/me"));
        Assert.NotNull((await context.Users.AsNoTracking().SingleAsync(u => u.Id == stored.Id)).LastLoginAt);
        // A valid token for a deleted user must no longer expose a profile.
        await context.Users.Where(u => u.Id == stored.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [SqlServerFact]
    public async Task ConcurrentCaseVariantsCannotCreateDuplicateAccounts()
    {
        await using var factory = new AuthApiFactory(fixture.ConnectionString);
        using var first = factory.CreateHttpsClient();
        using var second = factory.CreateHttpsClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/register", AuthEndpointTests.Registration(email)),
            second.PostAsJsonAsync("/api/auth/register", AuthEndpointTests.Registration($" {email.ToUpperInvariant()} ")));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(u => u.Email == email));
    }

    [SqlServerFact]
    public async Task ExistingCaseInsensitiveIndexRejectsDirectSqlCaseVariant()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var email = $"{Guid.NewGuid():N}@example.test";
        context.Users.Add(new TradingJournal.Domain.Entities.User { Email = email, DisplayName = "Test", PasswordHash = "test-only" });
        await context.SaveChangesAsync();
        context.Users.Add(new TradingJournal.Domain.Entities.User { Email = email.ToUpperInvariant(), DisplayName = "Test", PasswordHash = "test-only" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(Assert.IsType<Microsoft.Data.SqlClient.SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
    }
}
