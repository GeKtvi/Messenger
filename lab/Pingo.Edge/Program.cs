using Pingo.ServiceDefaults;

// The lab's stand-in for a global load balancer such as Azure Front Door: one address in front of the
// frontend and both APIs. Routes and destinations are in appsettings.json.
var builder = WebApplication.CreateBuilder(args);
builder.AddPingoTelemetry("pingo-edge");
builder.AddPingoHealth();

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapPingoHealth();
app.MapReverseProxy();

app.Run();
