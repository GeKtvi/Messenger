using MassTransit;
using Pingo.Contracts;

namespace Pingo.Messages.Api.Messaging;

// Greets a user in the chat when Identity reports a login. Like a controller, it only adapts the
// incoming event to IMessageService.
public class UserLoggedInConsumer(IMessageService messages) : IConsumer<UserLoggedIn>
{
    public async Task Consume(ConsumeContext<UserLoggedIn> context)
    {
        // RabbitMQ delivers at least once, so the same event can arrive twice. Its MessageId becomes
        // the chat message id, and the idempotent send (D-023) turns a repeat into a no-op.
        var id = context.MessageId ?? throw new InvalidOperationException("UserLoggedIn arrived without a MessageId.");
        await messages.SendAsync(id, $"Привет, {context.Message.Email}!", context.CancellationToken);
    }
}
