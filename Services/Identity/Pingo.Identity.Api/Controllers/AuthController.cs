using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pingo.Identity.Api.Contracts;
using Pingo.Identity.Models;

namespace Pingo.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    // Invalid bodies never reach here: [ApiController] returns 400 ValidationProblem automatically.
    [HttpPost("register")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<Results<Created<RegisterResponse>, Conflict<ProblemDetails>>> Register(
        RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request.Email, request.Password, ct);

        return result switch
        {
            RegistrationResult.Registered user =>
                TypedResults.Created((string?)null, new RegisterResponse(user.UserId, user.RegisteredAt)),
            RegistrationResult.EmailTaken => TypedResults.Conflict(new ProblemDetails
            {
                Title = "Email is already registered.",
                Status = StatusCodes.Status409Conflict,
            }),
            _ => throw new InvalidOperationException($"Unexpected registration result {result}."),
        };
    }

    [HttpPost("login")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.Email, request.Password, ct);

        return result switch
        {
            LoginResult.Succeeded login => TypedResults.Ok(ToResponse(login.Tokens)),
            LoginResult.InvalidCredentials => TypedResults.Problem(
                title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized),
            _ => throw new InvalidOperationException($"Unexpected login result {result}."),
        };
    }

    // Returns a new access token and a new refresh token; the refresh token sent in is used up.
    [HttpPost("refresh")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(request.RefreshToken, ct);

        return result switch
        {
            RefreshResult.Succeeded refresh => TypedResults.Ok(ToResponse(refresh.Tokens)),
            RefreshResult.InvalidToken => TypedResults.Problem(
                title: "Invalid or expired refresh token.", statusCode: StatusCodes.Status401Unauthorized),
            _ => throw new InvalidOperationException($"Unexpected refresh result {result}."),
        };
    }

    private static TokenResponse ToResponse(AuthTokens tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
}
