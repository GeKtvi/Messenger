using Messenger.Message.Api.Contracts;
using Messenger.Message.Api.Data;
using Messenger.Message.Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Message.Api.Endpoints;

public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/messages").WithTags("Messages");

        group.MapGet("/", GetAll).WithName("GetMessages");
        group.MapPost("/", Send).WithName("SendMessage");

        return app;
    }

    // Scaffold only: loads and returns every message with no paging or limit.
    // Fine for the first stage; replace with a paged/cursor-based query before real use.
    private static async Task<Ok<List<MessageResponse>>> GetAll(MessageDbContext db, CancellationToken ct)
    {
        var response = await db.Messages
            .AsNoTracking()
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageResponse(m.Id, m.Text, m.SentAt))
            .ToListAsync(ct);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Created<MessageResponse>, ValidationProblem>> Send(SendMessageRequest request, MessageDbContext db, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Text)] = ["Message text is required."]
            });

        if (text.Length > MessageDbContext.MaxTextLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Text)] = [$"Message text must be at most {MessageDbContext.MaxTextLength} characters."]
            });

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            Text = text,
            SentAt = DateTimeOffset.UtcNow
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var response = new MessageResponse(message.Id, message.Text, message.SentAt);
        return TypedResults.Created($"/api/messages/{message.Id}", response);
    }
}
