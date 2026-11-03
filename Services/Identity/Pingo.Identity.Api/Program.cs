using Hangfire;
using Pingo.Identity;
using Pingo.Identity.Api.Jobs;
using Pingo.Identity.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

// Picks the error message language from Accept-Language (or ?culture=ru); anything else gets English.
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ru")
    .AddSupportedUICultures("en", "ru"));

app.MapControllers();

app.Run();

// Lets the component tests start the app through WebApplicationFactory<Program>.
public partial class Program;
