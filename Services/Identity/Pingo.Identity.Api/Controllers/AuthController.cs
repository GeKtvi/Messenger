using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pingo.Identity.Api.Auth;
using Pingo.Identity.Api.Contracts;
using Pingo.Identity.Api.HttpResults;
using Pingo.Identity.Api.OpenApi;
using Pingo.Identity.Models;

namespace Pingo.Identity.Api.Controllers;

// Errors are RFC 7807 problems written by TypedResults.Problem() (wrapped in ConflictProblem and
// UnauthorizedProblem), not ControllerBase.Problem() as the stage 04 task suggests (D-047, D-048).
// The title is localized from the request's Accept-Language, so clients should tell errors apart
// by the stable "type" URN instead.
[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public class AuthController(IAuthService auth, IStringLocalizer<ErrorMessages> messages) : ControllerBase
{
    // Invalid bodies never reach here: [ApiController] returns 400 ValidationProblem automatically.
    [HttpPost("register")]
    [ProducesValidationProblem]
    public async Task<Results<Created<RegisterResponse>, ConflictProblem>> Register(
        RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request.Email, request.Password, ct);
        if (result.IsFailure)
        {
            return result.Error switch
            {
                RegistrationError.EmailTaken => TypedResults.EmailTaken(messages),
                _ => throw new InvalidOperationException($"Unexpected registration error {result.Error}."),
            };
        }

        var user = result.Value;
        return TypedResults.Created((string?)null, new RegisterResponse(user.UserId, user.RegisteredAt));
    }

    [HttpPost("login")]
    [ProducesValidationProblem]
    public async Task<Results<Ok<TokenResponse>, UnauthorizedProblem>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Email, request.Password, ct);
        if (result.IsFailure)
        {
            return result.Error switch
            {
                LoginError.UserNotFound => TypedResults.UserNotFound(messages),
                LoginError.WrongPassword => TypedResults.WrongPassword(messages),
                _ => throw new InvalidOperationException($"Unexpected login error {result.Error}."),
            };
        }

        return TypedResults.Ok(SignIn(result.Value));
    }

    // Exchanges the refresh token cookie for a new access token and a new cookie; the old token is
    // used up. A missing or invalid cookie is deleted, so the browser stops sending it.
    [HttpPost("refresh")]
    public async Task<Results<Ok<TokenResponse>, UnauthorizedProblem>> Refresh(CancellationToken ct)
    {
        var refreshToken = RefreshTokenCookie.Read(Request);
        var result = refreshToken is null ? RefreshError.InvalidToken : await auth.RefreshAsync(refreshToken, ct);
        if (result.IsFailure)
        {
            RefreshTokenCookie.Delete(Response);
            return result.Error switch
            {
                RefreshError.InvalidToken => TypedResults.InvalidRefreshToken(messages),
                _ => throw new InvalidOperationException($"Unexpected refresh error {result.Error}."),
            };
        }

        return TypedResults.Ok(SignIn(result.Value));
    }

    // Ends the session: the refresh token stops working and its cookie is deleted. Always 204, so
    // logging out twice, or without a session, is fine.
    [HttpPost("logout")]
    public async Task<NoContent> Logout(CancellationToken ct)
    {
        if (RefreshTokenCookie.Read(Request) is { } refreshToken)
            await auth.LogoutAsync(refreshToken, ct);

        RefreshTokenCookie.Delete(Response);
        return TypedResults.NoContent();
    }

    // The access token goes in the body for the client to hold; the refresh token only in the cookie.
    private TokenResponse SignIn(AuthTokens tokens)
    {
        RefreshTokenCookie.Write(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return new TokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshTokenExpiresAt);
    }
}
