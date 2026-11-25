using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Pingo.Frontend;
using Pingo.Frontend.Services;
using Pingo.Frontend.Services.Auth;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var messageApiUrl = builder.Configuration["MessageApi:BaseUrl"]
    ?? throw new InvalidOperationException("MessageApi:BaseUrl is not configured.");
var identityApiUrl = builder.Configuration["IdentityApi:BaseUrl"]
    ?? throw new InvalidOperationException("IdentityApi:BaseUrl is not configured.");

// A relative address like "/" means the APIs are on the same origin as the app, behind a reverse proxy.
var appAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddPingoApiClients(new Uri(appAddress, messageApiUrl), new Uri(appAddress, identityApiUrl));
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<MessageOutbox>();
builder.Services.AddScoped<ILiveMessages>(services => new LiveMessages(
    new Uri(new Uri(appAddress, messageApiUrl), "api/messages/hub"),
    services.GetRequiredService<IAuthSession>(),
    services.GetRequiredService<ILogger<LiveMessages>>()));
builder.Services.AddScoped<MessageFeed>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
