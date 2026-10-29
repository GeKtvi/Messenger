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

builder.Services.AddScoped(_ => new MessageApiClient(new HttpClient { BaseAddress = new Uri(messageApiUrl) }));
builder.Services.AddScoped<MessageOutbox>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
