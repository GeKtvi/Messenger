namespace Pingo.Identity.ComponentTests;

// All Identity test classes share one IdentityApiFactory, so PostgreSQL starts once per test run.
// xUnit runs the classes of one collection one after another, which Respawn's reset relies on.
[CollectionDefinition(Name)]
public sealed class IdentityCollection : ICollectionFixture<IdentityApiFactory>
{
    public const string Name = "Identity";
}

// Base for the test classes: empties the database after every test. Each test class still needs
// [Collection(IdentityCollection.Name)] and [Trait("Category", "Docker")] itself.
public abstract class IdentityTestBase(IdentityApiFactory factory) : IAsyncLifetime
{
    protected IdentityApiFactory Factory { get; } = factory;

    protected HttpClient Client { get; } = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await Factory.ResetDatabaseAsync();

        // Some tests move the shared clock by fractions of a second. The clock can't go back, so move
        // it on to the next whole second: every test starts on one.
        var pastSecond = Factory.Time.GetUtcNow().Ticks % TimeSpan.TicksPerSecond;
        if (pastSecond != 0)
            Factory.Time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - pastSecond));
    }

    // A client whose requests ask for the given language, e.g. "ru-RU".
    protected HttpClient CreateClient(string acceptLanguage)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        return client;
    }
}
