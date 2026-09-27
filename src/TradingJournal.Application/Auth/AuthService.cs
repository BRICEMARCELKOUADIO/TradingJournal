using System.ComponentModel.DataAnnotations;
using TradingJournal.Domain.Entities;

namespace TradingJournal.Application.Auth;

public sealed class AuthService(
    IUserRepository users, IPasswordHasher passwords, IAccessTokenService tokens, TimeProvider time)
{
    public async Task<UserResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var user = new User
        {
            DisplayName = request.DisplayName, Email = request.Email,
            PasswordHash = passwords.Hash(request.Password), CreatedAt = time.GetUtcNow().UtcDateTime
        };
        // Rely on the unique database index as well as normalization, including concurrent registrations.
        return await users.TryAddAsync(user, cancellationToken) ? ToResponse(user) : null;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        var user = await users.FindByEmailAsync(request.Email, cancellationToken);
        // The hasher performs equivalent expensive work for a missing user too.
        var valid = passwords.Verify(request.Password, user?.PasswordHash);
        if (user is null || !valid) return null;
        var token = tokens.Create(user.Id);
        user.LastLoginAt = time.GetUtcNow().UtcDateTime;
        await users.SaveChangesAsync(cancellationToken);
        return new LoginResponse(token.Value, token.ExpiresAtUtc, ToResponse(user));
    }

    public async Task<UserResponse?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        return user is null ? null : ToResponse(user);
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.DisplayName, user.Email);
}
