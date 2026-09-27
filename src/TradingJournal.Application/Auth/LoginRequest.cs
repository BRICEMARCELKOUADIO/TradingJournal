using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Application.Auth;

public sealed class LoginRequest
{
    private string email = string.Empty;

    [Required, EmailAddress, StringLength(255)]
    public string Email { get => email; set => email = EmailNormalizer.Normalize(value); }

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}
