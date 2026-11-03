using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Pingo.Messages.Api.Auth;

// How this service checks the access tokens the Identity service issues. The values must match
// Identity's "Jwt" settings: same issuer, same audience, same signing key (HS256 is symmetric).
public class JwtValidationOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;
}

public static class JwtAuthentication
{
    // Accepts "Authorization: Bearer <access token>" and rejects anything not signed by Identity's
    // key for this issuer and audience, or outside its lifetime. Bad settings stop the app at startup.
    public static IServiceCollection AddPingoJwtAuthentication(this IServiceCollection services, IConfigurationSection jwtSection)
    {
        services.AddOptions<JwtValidationOptions>().Bind(jwtSection).ValidateDataAnnotations().ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtValidationOptions>>((bearer, jwt) =>
            {
                // Keep claim names as they are in the token ("sub"), instead of mapping them to the
                // long WS-Federation names.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    // The default allows 5 minutes past "exp"; a little slack for clock drift is enough.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }
}
