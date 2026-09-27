using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TradingJournal.Application.Auth;

namespace TradingJournal.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    [RequestSizeLimit(16_384)]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await auth.RegisterAsync(request, cancellationToken);
        return user is null
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Inscription impossible avec cet email.")
            : Created("/api/auth/me", user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(16_384)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request, cancellationToken);
        return result is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Email ou mot de passe incorrect.")
            : Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid id) return Unauthorized();
        var user = await auth.GetUserAsync(id, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }
}
