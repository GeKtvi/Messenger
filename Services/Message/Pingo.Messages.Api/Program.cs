using Pingo.Messages;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddMessages(builder.Configuration.GetConnectionString("Messages"));

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

await app.Services.MigrateMessagesDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(FrontendCorsPolicy);

app.MapControllers();

app.Run();

// Lets component tests start the app with WebApplicationFactory<Program>.
public partial class Program;
