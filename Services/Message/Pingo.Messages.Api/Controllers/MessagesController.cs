using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pingo.Messages.Api.Contracts;
using Pingo.Messages.Models;

namespace Pingo.Messages.Api.Controllers;

// Every endpoint needs a valid access token from the Identity service; without one the answer is 401.
[ApiController]
[Authorize]
[Route("api/messages")]
[Tags("Messages")]
public class MessagesController(IMessageService messages) : ControllerBase
{
    [HttpGet]
    public async Task<Ok<List<MessageResponse>>> GetAll(CancellationToken ct)
    {
        var all = await messages.GetAllAsync(ct);
        return TypedResults.Ok(all.Select(ToResponse).ToList());
    }

    // Idempotent send: the client picks the id. A new id creates the message (201),
    // an existing id updates its text (200). Repeating the same request changes nothing.
    // Invalid bodies never reach here: [ApiController] returns 400 ValidationProblem automatically.
    [HttpPut("{messageId:guid}")]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<Results<Created<MessageResponse>, Ok<MessageResponse>, ValidationProblem>> Send(
        Guid messageId, SendMessageRequest request, CancellationToken ct)
    {
        if (messageId == Guid.Empty)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(messageId)] = ["Message id must not be empty."]
            });

        var result = await messages.SendAsync(messageId, request.Text, ct);
        var response = ToResponse(result.Message);

        return result.Created
            ? TypedResults.Created($"/api/messages/{response.Id}", response)
            : TypedResults.Ok(response);
    }

    private static MessageResponse ToResponse(Message message) =>
        new(message.Id, message.Text, message.SentAt, message.UpdatedAt);
}
