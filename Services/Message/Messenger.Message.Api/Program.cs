using Messenger.Message.Api.Data;
using Messenger.Message.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<MessageDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Messages")));

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Simple for now: create the schema on startup. Switch to migrations once the model settles.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<MessageDbContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(FrontendCorsPolicy);

app.MapMessageEndpoints();

app.Run();
