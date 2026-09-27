using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Application.Auth;

public sealed class RegisterRequest
{
    private string displayName = string.Empty;
    private string email = string.Empty;

    [Required, StringLength(100)]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? string.Empty; }

    [Required, EmailAddress, StringLength(255)]
    public string Email { get => email; set => email = EmailNormalizer.Normalize(value); }

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}
