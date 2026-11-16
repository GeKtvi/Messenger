using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pingo.ServiceDefaults;

// Distributed tracing shared by the services (stage 06). Each service records spans for incoming
// HTTP requests, outgoing HTTP calls, PostgreSQL commands and MassTransit publish/consume, and sends
// them to an OTLP collector (Jaeger locally). The trace context travels in the "traceparent" HTTP
// header and in MassTransit's message headers, so a login and the greeting it causes end up in one trace.
public static class TelemetrySetup
{
    // MassTransit's ActivitySource name.
    private const string MassTransitSource = "MassTransit";

    // Without "OpenTelemetry:OtlpEndpoint" (e.g. in tests) spans are still recorded, just not exported.
    public static IHostApplicationBuilder AddPingoTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddSource(MassTransitSource);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            });

        return builder;
    }
}
