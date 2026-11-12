using Microsoft.AspNetCore.Components.Authorization;
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

// Sign-in: the session holds the access token in memory; Identity keeps the refresh token in an
// HttpOnly cookie (D-054).
builder.Services.AddHttpClient<IdentityApiClient>(client => client.BaseAddress = new Uri(identityApiUrl));
builder.Services.AddSingleton<AuthSession>();
builder.Services.AddSingleton<AuthenticationStateProvider, PingoAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// Every Message API request carries the access token.
builder.Services.AddTransient<AccessTokenHandler>();
builder.Services.AddHttpClient<MessageApiClient>(client => client.BaseAddress = new Uri(messageApiUrl))
    .AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddScoped<MessageOutbox>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
