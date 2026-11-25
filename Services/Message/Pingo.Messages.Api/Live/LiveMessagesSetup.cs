using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Pingo.Messages.Api.Contracts;
using Pingo.Messages.Models;

namespace Pingo.Messages.Api.Live;

// Published over RabbitMQ when a message is saved. Every instance of this service, in every region,
// receives it and passes it to the chats connected to that instance (see MessageSavedConsumer).
public record MessageSaved(Guid Id, string Text, DateTimeOffset SentAt, DateTimeOffset? UpdatedAt);

public static class LiveMessagesSetup
{
    public static IServiceCollection AddPingoLiveMessages(this IServiceCollection services)
    {
        services.AddSignalR(options =>
        {
            // Below the edge's 10 s limit for a connection without traffic (D-067).
            options.KeepAliveInterval = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<IMessageNotifier, MessageSavedPublisher>();
        return services;
    }

    // Unlike UserLoggedInConsumer, whose queue the instances share, each instance needs every event:
    // a temporary queue of its own, deleted when the instance stops.
    public static void AddMessageSavedConsumer(this IBusRegistrationConfigurator bus) =>
        bus.AddConsumer<MessageSavedConsumer>().Endpoint(endpoint =>
        {
            endpoint.Temporary = true;
            endpoint.InstanceId = NewId.NextGuid().ToString("N");
        });
}

// Publishes in the background: the send doesn't wait for RabbitMQ, which from the US or Asia is a long
// trip away. If publishing fails, open chats miss the message until they next load the list.
internal sealed class MessageSavedPublisher(IBus bus, ILogger<MessageSavedPublisher> logger) : IMessageNotifier
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public Task MessageSavedAsync(Message message, CancellationToken ct = default)
    {
        _ = PublishAsync(new MessageSaved(message.Id, message.Text, message.SentAt, message.UpdatedAt));
        return Task.CompletedTask;
    }

    private async Task PublishAsync(MessageSaved saved)
    {
        try
        {
            // Not the request's token: the request may well have ended by then.
            using var timeout = new CancellationTokenSource(PublishTimeout);
            await bus.Publish(saved, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish MessageSaved for message {MessageId}", saved.Id);
        }
    }
}

public sealed class MessageSavedConsumer(IHubContext<MessagesHub> hub) : IConsumer<MessageSaved>
{
    public Task Consume(ConsumeContext<MessageSaved> context)
    {
        var saved = context.Message;
        return hub.Clients.All.SendAsync(MessagesHub.MessageSaved,
            new MessageResponse(saved.Id, saved.Text, saved.SentAt, saved.UpdatedAt), context.CancellationToken);
    }
}
