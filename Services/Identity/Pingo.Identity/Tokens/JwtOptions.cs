using System.ComponentModel.DataAnnotations;

namespace Pingo.Identity.Tokens;

// Settings for the access tokens (JWTs) this service issues. Bound from the "Jwt" configuration
// section and checked on startup, so a bad value stops the app instead of failing on the first login.
//
// A JWT is three base64url parts joined by dots: header.payload.signature.
// - The header says how it's signed ({"alg":"HS256","typ":"JWT"}).
// - The payload holds the claims: who the token is for, who issued it, for whom, and until when.
// - The signature is HMAC-SHA256(header + "." + payload, SigningKey). Anyone can read a JWT, but only
//   someone with the key can produce a valid signature, so a changed payload is detected.
public class JwtOptions
{
    // The configuration section these settings are read from: "Jwt": { ... } in appsettings.json.
    public const string SectionName = "Jwt";

    // Who issued the token. Written into the "iss" claim. A service checking the token rejects it
    // unless "iss" matches what it expects, so tokens from some other issuer don't count.
    [Required]
    public string Issuer { get; set; } = string.Empty;

    // Who the token is meant for. Written into the "aud" claim. A service only accepts tokens whose
    // "aud" names it, so a token issued for a different app can't be replayed here.
    [Required]
    public string Audience { get; set; } = string.Empty;

    // The secret used to sign tokens (and, with HS256, to check them too). Whoever has it can create
    // valid tokens for any user, so it must never be committed for a real environment: it comes from
    // an environment variable or a secret store there. HMAC-SHA256 needs at least 256 bits (32 bytes).
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    // How long an access token works. Written into the "exp" claim. A JWT can't be revoked once
    // issued (services check it on their own, without asking us), so a short lifetime limits the
    // damage if one leaks. When it expires, the client uses the refresh token to get a new one.
    // At least a minute: a zero lifetime would make every token invalid as it's issued.
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    // How long a refresh token works. It isn't a JWT: it's a random string that is only valid while
    // its hash is in our database, so it can be revoked at any time. This is how long a user stays
    // signed in without entering the password again.
    [Range(typeof(TimeSpan), "00:01:00", "365.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}
