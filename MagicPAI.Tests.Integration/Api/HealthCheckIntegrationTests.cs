using System.Net;
using MagicPAI.Tests.Integration.Fixtures;

namespace MagicPAI.Tests.Integration.Api;

public class HealthCheckIntegrationTests : IntegrationTestBase
{
    public HealthCheckIntegrationTests(MagicPaiWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthLive_Returns200()
    {
        var response = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_Returns200()
    {
        var response = await Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
