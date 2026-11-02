using System.ComponentModel.DataAnnotations;

namespace Pingo.Identity.Api.Contracts;

public class RegisterRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email is not a valid email address.")]
    [MaxLength(254, ErrorMessage = "Email must be at most {1} characters.")]
    public string Email { get; init; } = string.Empty;

    // Length is the only rule: NIST SP 800-63B advises against composition rules (digits, symbols)
    // and recommends allowing long passphrases.
    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least {1} characters.")]
    [MaxLength(128, ErrorMessage = "Password must be at most {1} characters.")]
    public string Password { get; init; } = string.Empty;
}

public record RegisterResponse(Guid UserId, DateTimeOffset RegisteredAt);

// No length or format rules here: they belong to registration, and the rules may change later.
public class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = string.Empty;
}

public record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt)
{
    // Tells the client how to send the access token: "Authorization: Bearer <accessToken>".
    public string TokenType => "Bearer";
}
