using System.Net.Http.Headers;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pingo.Messages.Data;

namespace Pingo.Messages.ComponentTests;

// Starts the real Message service in memory. Used as an xUnit class fixture, so the app starts
// once per test class instead of once per test. Subclasses choose the database.
public abstract class MessagesApiFactoryBase : WebApplicationFactory<Program>
{
    // Tests control "now" through this clock instead of the system clock.
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    protected abstract void ConfigureDatabase(DbContextOptionsBuilder options);

    // Gives each test an empty database, while the app itself keeps running.
    public abstract Task ResetDatabaseAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // AddMessages requires a connection string. It's never used: the database is replaced below.
        builder.UseSetting("ConnectionStrings:Messages", "Host=unused-in-tests");
        // appsettings.Development.json isn't loaded in Testing, so the key comes from here.
        builder.UseSetting("Jwt:SigningKey", TestAccessTokens.SigningKey);
        // Never connected to: the test harness below replaces RabbitMQ.
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://unused-in-tests/");

        builder.ConfigureTestServices(services =>
        {
            // Replace the app's database setup. EF Core 9+ stores the provider setup in
            // IDbContextOptionsConfiguration, so that has to go too.
            services.RemoveAll<DbContextOptions<MessageDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MessageDbContext>>();
            services.AddDbContext<MessageDbContext>(ConfigureDatabase);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // MassTransit's in-memory test harness instead of RabbitMQ: same consumers, and tests can
            // publish events and see what was consumed.
            services.AddMassTransitTestHarness(harness =>
                harness.SetTestTimeouts(testInactivityTimeout: TimeSpan.FromSeconds(1)));
        });
    }

    // Every client sends a valid access token, as a signed-in frontend would. Tests about
    // authorization clear or replace the header.
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAccessTokens.Create());
    }

    private protected MessageDbContext CreateDbContext(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<MessageDbContext>();
}
