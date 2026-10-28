using Messenger.Message.Api.Contracts;
using Messenger.Message.Api.Data;
using Messenger.Message.Api.Entities;
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

    private static async Task<IResult> GetAll(MessageDbContext db, CancellationToken ct)
    {
        // SQLite can't ORDER BY DateTimeOffset server-side, so sort after loading.
        var messages = await db.Messages.AsNoTracking().ToListAsync(ct);

        var response = messages
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageResponse(m.Id, m.Text, m.SentAt));

        return Results.Ok(response);
    }

    private static async Task<IResult> Send(SendMessageRequest request, MessageDbContext db, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Text)] = ["Message text is required."]
            });

        if (text.Length > MessageDbContext.MaxTextLength)
            return Results.ValidationProblem(new Dictionary<string, string[]>
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
        return Results.Created($"/api/messages/{message.Id}", response);
    }
}
