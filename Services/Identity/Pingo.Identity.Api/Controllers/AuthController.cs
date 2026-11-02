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
}
