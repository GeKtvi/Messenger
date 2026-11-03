using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pingo.Messages.ComponentTests;

// Access tokens shaped like the Identity service's (HS256; sub, jti, iss, aud, iat, nbf, exp), so the
// Message service can be tested without running Identity. Each argument can be changed to build a
// token the service must reject.
public static class TestAccessTokens
{
    public const string SigningKey = "pingo-component-tests-signing-key-not-a-secret";
    public const string Issuer = "pingo-identity";
    public const string Audience = "pingo";

    // JwtBearer checks the lifetime against the real clock, not the tests' FakeTimeProvider.
    public static string Create(
        Guid? userId = null,
        string issuer = Issuer,
        string audience = Audience,
        string signingKey = SigningKey,
        DateTime? expiresAt = null)
    {
        var expires = expiresAt ?? DateTime.UtcNow.AddMinutes(15);
        var issuedAt = expires.AddMinutes(-15);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, (userId ?? Guid.NewGuid()).ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        });
    }
}
