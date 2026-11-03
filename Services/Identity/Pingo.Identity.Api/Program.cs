using Pingo.Identity;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI over the built-in OpenAPI document, for trying the endpoints by hand.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Pingo Identity"));
}

// Picks the error message language from Accept-Language (or ?culture=ru); anything else gets English.
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ru")
    .AddSupportedUICultures("en", "ru"));

app.MapControllers();

app.Run();
