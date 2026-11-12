using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Pingo.Frontend;
using Pingo.Frontend.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var messageApiUrl = builder.Configuration["MessageApi:BaseUrl"]
    ?? throw new InvalidOperationException("MessageApi:BaseUrl is not configured.");
var identityApiUrl = builder.Configuration["IdentityApi:BaseUrl"]
    ?? throw new InvalidOperationException("IdentityApi:BaseUrl is not configured.");

builder.Services.AddPingoApiClients(new Uri(messageApiUrl), new Uri(identityApiUrl));
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<MessageOutbox>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
