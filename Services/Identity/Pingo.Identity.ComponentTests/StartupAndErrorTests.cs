using System.Net;
using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pingo.Identity.Models;

namespace Pingo.Identity.ComponentTests;

// How the app behaves when something is wrong outside a request's control: bad settings, a crash.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class StartupAndErrorTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    [Theory]
    [InlineData("Jwt:SigningKey", "too-short")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:AccessTokenLifetime", "00:00:00")]
    [InlineData("Jwt:RefreshTokenLifetime", "00:00:00")]
    public void Start_InvalidJwtSetting_FailsInsteadOfServingRequests(string key, string value)
    {
        using var app = Factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        var start = () => app.CreateClient();

        start.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task UnhandledException_Returns500ProblemWithoutTheDetails()
    {
        await using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.AddScoped<IAuthService, FailingAuthService>();
        }));

        var response = await app.CreateClient().LoginAsync("ann@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(FailingAuthService.Secret);
        var problem = await response.ReadAsync<ProblemDetails>();
        problem.Status.Should().Be(500);
        problem.Extensions.Should().ContainKey("traceId");
    }

    private sealed class FailingAuthService : IAuthService
    {
        public const string Secret = "connection string with a password";

        public Task<Result<RegisteredUser, RegistrationError>> RegisterAsync(
            string email, string password, CancellationToken ct = default) => throw new InvalidOperationException(Secret);

        public Task<Result<AuthTokens, LoginError>> LoginAsync(
            string email, string password, CancellationToken ct = default) => throw new InvalidOperationException(Secret);

        public Task<Result<AuthTokens, RefreshError>> RefreshAsync(
            string refreshToken, CancellationToken ct = default) => throw new InvalidOperationException(Secret);

        public Task LogoutAsync(string refreshToken, CancellationToken ct = default) =>
            throw new InvalidOperationException(Secret);
    }
}
