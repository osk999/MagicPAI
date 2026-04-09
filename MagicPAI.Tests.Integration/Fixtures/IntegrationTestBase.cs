namespace MagicPAI.Tests.Integration.Fixtures;

/// <summary>
/// Base class for integration tests. Provides shared factory access.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<MagicPaiWebApplicationFactory>
{
    protected readonly MagicPaiWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(MagicPaiWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }
}
