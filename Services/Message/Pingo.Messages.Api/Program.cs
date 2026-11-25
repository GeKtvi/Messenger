using Pingo.Messages;
using Pingo.Messages.Api.Auth;
using Pingo.Messages.Api.Messaging;
using Pingo.ServiceDefaults;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);
builder.AddPingoTelemetry("pingo-messages");
builder.AddPingoHealth();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddMessages(
    builder.Configuration.GetConnectionString("Messages"),
    builder.Configuration.GetConnectionString("MessagesRead"));
builder.Services.AddPingoJwtAuthentication(builder.Configuration.GetSection(JwtValidationOptions.SectionName));
builder.Services.AddPingoMessaging(builder.Configuration.GetConnectionString("RabbitMq"));

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

// CORS runs first, so preflight requests (which never carry a token) succeed, and a 401 still has
// the CORS headers the browser needs to let the frontend read it and refresh its token.
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPingoHealth();

app.Run();

// Lets component tests start the app with WebApplicationFactory<Program>.
public partial class Program;
