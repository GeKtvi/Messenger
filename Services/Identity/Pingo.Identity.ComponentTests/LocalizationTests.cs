using System.Net;
using FluentAssertions;

namespace Pingo.Identity.ComponentTests;

// Every error has a Russian title, and the language is picked from the request. A missing or
// misspelled key in ErrorMessages.ru.resx would show up here as an English title or the key itself.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class LocalizationTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task EmailTaken_InRussian()
    {
        using var client = CreateClient("ru-RU");
        await client.RegisterAsync(Email);

        var response = await client.RegisterAsync(Email);

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict, "email-taken", "Пользователь с таким email уже существует.");
    }

    [Fact]
    public async Task UserNotFound_InRussian()
    {
        using var client = CreateClient("ru-RU");

        var response = await client.LoginAsync(Email);

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "user-not-found", "Пользователь с таким email не найден.");
    }

    [Fact]
    public async Task WrongPassword_InRussian()
    {
        using var client = CreateClient("ru-RU");
        await client.RegisterAsync(Email);

        var response = await client.LoginAsync(Email, "wrong password!");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "wrong-password", "Неверный пароль.");
    }

    [Fact]
    public async Task InvalidRefreshToken_InRussian()
    {
        using var client = CreateClient("ru");

        var response = await client.RefreshAsync("not-a-real-token");

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "invalid-refresh-token", "Refresh-токен недействителен или истёк.");
    }

    [Fact]
    public async Task CultureQueryString_WithoutHeader_PicksRussian()
    {
        var response = await Client.SendAsync(IdentityHttpClientExtensions.WithRefreshCookie(
            HttpMethod.Post, "/api/auth/refresh?culture=ru", "not-a-real-token"));

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "invalid-refresh-token", "Refresh-токен недействителен или истёк.");
    }

    [Fact]
    public async Task PreferredLanguageUnsupported_FallsBackToTheNextOne()
    {
        using var client = CreateClient("de-DE, ru;q=0.8");

        var response = await client.RefreshAsync("not-a-real-token");

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "invalid-refresh-token", "Refresh-токен недействителен или истёк.");
    }

    [Fact]
    public async Task UnsupportedLanguageOnly_FallsBackToEnglish()
    {
        using var client = CreateClient("de-DE");

        var response = await client.RefreshAsync("not-a-real-token");

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "invalid-refresh-token", "The refresh token is invalid or has expired.");
    }
}
