using Pingo.Messages.Api.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pingo.Messages;
using Pingo.Messages.Models;

namespace Pingo.Messages.Api.Controllers;

[ApiController]
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

    // Invalid requests never reach here: [ApiController] returns 400 ValidationProblem automatically.
    [HttpPost]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<Created<MessageResponse>> Send(SendMessageRequest request, CancellationToken ct)
    {
        var message = await messages.SendAsync(request.Text, ct);
        return TypedResults.Created($"/api/messages/{message.Id}", ToResponse(message));
    }

    private static MessageResponse ToResponse(Message message) =>
        new(message.Id, message.Text, message.SentAt, message.UpdatedAt);
}
