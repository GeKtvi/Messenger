using MassTransit;

namespace Pingo.Messages.Api.Messaging;

// MassTransit over RabbitMQ (stage 06). The Message service consumes Identity's events.
public static class MessagingSetup
{
    public static IServiceCollection AddPingoMessaging(this IServiceCollection services, string? rabbitMqConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);

        services.AddMassTransit(bus =>
        {
            // Queue "user-logged-in". Several Message service instances share it, so each event is
            // handled once.
            bus.SetKebabCaseEndpointNameFormatter();

            // A failed attempt (say, the database briefly unavailable) is retried a few times in
            // memory; after that the event goes to the "user-logged-in_error" queue instead of being lost.
            bus.AddConsumer<UserLoggedInConsumer>(consumer =>
                consumer.UseMessageRetry(retry => retry.Intervals(200, 1000, 5000)));

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(rabbitMqConnectionString));
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
