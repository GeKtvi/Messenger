using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pingo.ServiceDefaults;

// GET /health: is this instance alive and serving requests? A load balancer stops sending traffic to
// an instance that doesn't answer.
public static class HealthSetup
{
    public const string HealthPath = "/health";

    public static IHostApplicationBuilder AddPingoHealth(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
        return builder;
    }

    // No checks run: all instances share PostgreSQL and RabbitMQ, so if one of those is down, every
    // instance would fail at once and none would be left to route to. The filter also leaves out the
    // bus check MassTransit registers by itself.
    public static void MapPingoHealth(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHealthChecks(HealthPath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
}
