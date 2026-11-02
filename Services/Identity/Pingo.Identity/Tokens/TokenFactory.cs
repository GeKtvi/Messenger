using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pingo.Identity.Tokens;

internal record AccessToken(string Value, DateTimeOffset ExpiresAt);

internal record RefreshToken(string Value, byte[] Hash, DateTimeOffset ExpiresAt);

internal class TokenFactory(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _signingCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)), SecurityAlgorithms.HmacSha256);

    // A signed JWT that other services can check without calling Identity. It carries only the user id.
    public AccessToken CreateAccessToken(Guid userId, DateTimeOffset now)
    {
        var expiresAt = now + _options.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
        });

        return new AccessToken(token, expiresAt);
    }

    // A random string with no meaning of its own: it only works while its hash is in the database.
    public RefreshToken CreateRefreshToken(DateTimeOffset now)
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new RefreshToken(value, HashRefreshToken(value), now + _options.RefreshTokenLifetime);
    }

    // Only the hash is stored, so a leaked table can't be used to sign in. A plain SHA-256 is enough
    // here, unlike for passwords: the token is 256 random bits, so there's nothing to guess.
    public static byte[] HashRefreshToken(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
