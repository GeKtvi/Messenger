using MassTransit;

namespace Pingo.Identity.Api.Messaging;

// MassTransit over RabbitMQ (stage 06). Identity only publishes events (UserLoggedIn); it has no
// consumers yet, so it declares no queues of its own.
public static class MessagingSetup
{
    public static IServiceCollection AddPingoMessaging(this IServiceCollection services, string? rabbitMqConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();
            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(rabbitMqConnectionString));
                rabbit.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
