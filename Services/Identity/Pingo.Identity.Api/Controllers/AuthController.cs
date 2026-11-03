using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Pingo.Identity.Api.Contracts;
using Pingo.Identity.Api.HttpResults;
using Pingo.Identity.Api.OpenApi;
using Pingo.Identity.Models;
using LoginResults = Pingo.Identity.Models.LoginResults;
using RefreshResults = Pingo.Identity.Models.RefreshResults;
using RegistrationResults = Pingo.Identity.Models.RegistrationResults;

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

        return result switch
        {
            RegistrationResults.Registered user =>
                TypedResults.Created((string?)null, new RegisterResponse(user.UserId, user.RegisteredAt)),
            RegistrationResults.EmailTaken =>
                TypedResults.EmailTaken(messages),
            _ => throw new InvalidOperationException($"Unexpected registration result {result}."),
        };
    }

    [HttpPost("login")]
    [ProducesValidationProblem]
    public async Task<Results<Ok<TokenResponse>, UnauthorizedProblem>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Email, request.Password, ct);

        return result switch
        {
            LoginResults.Succeeded login => TypedResults.Ok(ToResponse(login.Tokens)),
            LoginResults.UserNotFound =>
                TypedResults.UserNotFound(messages),
            LoginResults.WrongPassword =>
                TypedResults.WrongPassword(messages),
            _ => throw new InvalidOperationException($"Unexpected login result {result}."),
        };
    }

    // Returns a new access token and a new refresh token; the refresh token sent in is used up.
    [HttpPost("refresh")]
    [ProducesValidationProblem]
    public async Task<Results<Ok<TokenResponse>, UnauthorizedProblem>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, ct);

        return result switch
        {
            RefreshResults.Succeeded refresh => TypedResults.Ok(ToResponse(refresh.Tokens)),
            RefreshResults.InvalidToken =>
                TypedResults.InvalidRefreshToken(messages),
            _ => throw new InvalidOperationException($"Unexpected refresh result {result}."),
        };
    }

    private static TokenResponse ToResponse(AuthTokens tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
}
