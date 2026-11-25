using Pingo.Edge;
using Pingo.ServiceDefaults;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms;

// The lab's stand-in for a global load balancer such as Azure Front Door: one address in front of the
// frontend and both APIs. Routes are in appsettings.json; each edge container adds the addresses at
// which it reaches the regions, and the order in which it prefers them.
var builder = WebApplication.CreateBuilder(args);
builder.AddPingoTelemetry("pingo-edge");
builder.AddPingoHealth();

builder.Services.AddSingleton<ILoadBalancingPolicy, RegionOrderPolicy>();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // Without a limit, a request to a region that is gone can hang for several seconds before failing.
    .ConfigureHttpClient((_, handler) => handler.ConnectTimeout = TimeSpan.FromSeconds(2))
    // Shows which region answered.
    .AddTransforms(transforms => transforms.AddResponseTransform(context =>
    {
        var destination = context.HttpContext.GetReverseProxyFeature().ProxiedDestination;
        context.HttpContext.Response.Headers["X-Pingo-Region"] = destination?.DestinationId;
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

app.MapPingoHealth();
app.MapReverseProxy();

app.Run();

// Lets the tests start the edge through WebApplicationFactory<Program>.
public partial class Program;
