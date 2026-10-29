using Pingo.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddPingoIdentity();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI over the built-in OpenAPI document, for trying the endpoints by hand.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Pingo Identity"));
}

app.MapControllers();

app.Run();
