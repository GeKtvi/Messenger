using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Pingo.Contracts;
using Pingo.Identity.Data;
using Pingo.Identity.Models;
using Pingo.Identity.Passwords;
using Pingo.Identity.Tokens;

namespace Pingo.Identity.UnitTests;

// AuthService.LoginAsync on its own: no web host, no database, no broker. Every dependency with I/O
// is a Moq mock; TokenFactory is real, because it only computes. Login reports failures as
// LoginError values in a Result rather than exceptions (D-049), so the "throws" scenarios of the
// homework are checked as errors here (D-061).
public class AuthServiceLoginTests
{
    private const string Email = "ann@example.com";
    private const string Password = "correct horse battery";

    private static readonly PasswordHash StoredHash = new([1, 2, 3], [4, 5, 6], 600_000);
    private static readonly PasswordLogin AnnsLogin =
        new(Guid.NewGuid(), Email, StoredHash, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IPasswordLoginRepository> _passwordLogins = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IPasswordHasher> _passwords = new();
    private readonly Mock<IPublishEndpoint> _events = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly AuthService _auth;

    public AuthServiceLoginTests()
    {
        var jwt = Options.Create(new JwtOptions
        {
            Issuer = "pingo-identity",
            Audience = "pingo",
            SigningKey = "pingo-unit-tests-signing-key-not-a-secret",
        });

        _auth = new AuthService(
            _users.Object, _passwordLogins.Object, _refreshTokens.Object, _passwords.Object,
            new TokenFactory(jwt), _events.Object, _time, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task UserFoundAndPasswordCorrect_ReturnsTokensAndPublishesUserLoggedIn()
    {
        UserExists();
        PasswordIs(correct: true);

        var result = await _auth.LoginAsync(Email, Password);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrEmpty();
        result.Value.AccessTokenExpiresAt.Should().Be(_time.GetUtcNow().AddMinutes(15));
        result.Value.RefreshToken.Should().NotBeNullOrEmpty();
        result.Value.RefreshTokenExpiresAt.Should().Be(_time.GetUtcNow().AddDays(30));

        _refreshTokens.Verify(r => r.AddAsync(
            It.Is<StoredRefreshToken>(t => t.UserId == AnnsLogin.UserId &&
                t.TokenHash.SequenceEqual(TokenFactory.HashRefreshToken(result.Value.RefreshToken))),
            It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Publish(
            It.Is<UserLoggedIn>(m => m.Email == Email), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserNotFound_ReturnsUserNotFound_AndDoesNothingElse()
    {
        _passwordLogins
            .Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordLogin?)null);

        var result = await _auth.LoginAsync(Email, Password);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoginError.UserNotFound);
        _passwords.Verify(p => p.Verify(It.IsAny<string>(), It.IsAny<PasswordHash>()), Times.Never);
        NothingWasStoredOrPublished();
    }

    [Fact]
    public async Task WrongPassword_ReturnsWrongPassword_AndDoesNothingElse()
    {
        UserExists();
        PasswordIs(correct: false);

        var result = await _auth.LoginAsync(Email, "wrong password!");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoginError.WrongPassword);
        _passwords.Verify(p => p.Verify("wrong password!", StoredHash), Times.Once);
        NothingWasStoredOrPublished();
    }

    [Fact]
    public async Task EmailIsNormalized_BeforeTheLookupAndInTheEvent()
    {
        UserExists();
        PasswordIs(correct: true);

        await _auth.LoginAsync("  Ann@Example.COM ", Password);

        _passwordLogins.Verify(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Publish(
            It.Is<UserLoggedIn>(m => m.Email == Email), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishFails_TheLoginStillSucceeds()
    {
        // The login has already happened; a broker outage only loses the greeting (D-059).
        UserExists();
        PasswordIs(correct: true);
        _events
            .Setup(e => e.Publish(It.IsAny<UserLoggedIn>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ is down"));

        var result = await _auth.LoginAsync(Email, Password);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishHappensAfterTheRefreshTokenIsStored()
    {
        // Other services hear about a login only once it's complete.
        UserExists();
        PasswordIs(correct: true);
        var calls = new List<string>();
        _refreshTokens
            .Setup(r => r.AddAsync(It.IsAny<StoredRefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("store refresh token"))
            .Returns(Task.CompletedTask);
        _events
            .Setup(e => e.Publish(It.IsAny<UserLoggedIn>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("publish"))
            .Returns(Task.CompletedTask);

        await _auth.LoginAsync(Email, Password);

        calls.Should().Equal("store refresh token", "publish");
    }

    private void UserExists() =>
        _passwordLogins
            .Setup(r => r.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnnsLogin);

    private void PasswordIs(bool correct) =>
        _passwords
            .Setup(p => p.Verify(It.IsAny<string>(), StoredHash))
            .Returns(correct);

    private void NothingWasStoredOrPublished()
    {
        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<StoredRefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _events.Verify(e => e.Publish(It.IsAny<UserLoggedIn>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
