using System.Net;
using System.Net.Http.Json;
using MagicPAI.Server.Controllers;
using MagicPAI.Tests.Integration.Fixtures;

namespace MagicPAI.Tests.Integration.Api;

public class BrowseControllerIntegrationTests : IntegrationTestBase
{
    public BrowseControllerIntegrationTests(MagicPaiWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Browse_NoPath_ReturnsDriveRoots()
    {
        var response = await Client.GetAsync("/api/browse");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BrowseResult>();
        Assert.NotNull(result);
        Assert.True(result.Directories.Count > 0);
    }

    [Fact]
    public async Task Browse_NonexistentPath_Returns404()
    {
        var response = await Client.GetAsync("/api/browse?path=/nonexistent/path/does/not/exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkflows_ReturnsWorkflowOptions()
    {
        var response = await Client.GetAsync("/api/browse/workflows");
        response.EnsureSuccessStatusCode();

        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowOption>>();
        Assert.NotNull(workflows);
        Assert.True(workflows.Count >= 10);
        Assert.Contains(workflows, w => w.Id == "full-orchestrate" && w.IsDefault);
    }
}
