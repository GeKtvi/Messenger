using Hangfire;
using Pingo.Identity;
using Pingo.Identity.Api.Jobs;
using Pingo.Identity.Api.Messaging;
using Pingo.Identity.Tokens;
using Pingo.ServiceDefaults;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);
builder.AddPingoTelemetry("pingo-identity");

builder.Services.AddControllers();
// TypedResults.Problem() writes through IProblemDetailsService when it's registered, which adds the
// traceId that MVC's own 400 validation problems carry.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddPingoIdentity(
    builder.Configuration.GetConnectionString("Identity"),
    builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddPingoBackgroundJobs(builder.Configuration.GetConnectionString("Redis"));
builder.Services.AddPingoMessaging(builder.Configuration.GetConnectionString("RabbitMq"));

// The frontend calls Identity from another origin and needs the refresh token cookie sent along,
// so credentials are allowed. That only works with exact origins, never "*".
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(origins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Turns an unhandled exception into a 500 Problem Details response (with a traceId, without the
// exception's details), so even a crash answers in the same format as every other error.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI over the built-in OpenAPI document, for trying the endpoints by hand.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Pingo Identity"));
    // Hangfire's dashboard: scheduled, running and finished jobs. It only accepts local requests.
    app.UseHangfireDashboard("/hangfire");
}

app.ScheduleRecurringJobs();

app.UseCors(FrontendCorsPolicy);

// Picks the error message language from Accept-Language (or ?culture=ru); anything else gets English.
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ru")
    .AddSupportedUICultures("en", "ru"));

app.MapControllers();

app.Run();

// Lets the component tests start the app through WebApplicationFactory<Program>.
public partial class Program;
