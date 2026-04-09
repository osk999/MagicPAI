using MagicPAI.Server.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace MagicPAI.Tests.Server;

public class BrowseControllerTests
{
    private readonly BrowseController _controller = new();

    [Fact]
    public void Browse_NoPath_ReturnsDriveRoots()
    {
        var result = _controller.Browse(null);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var browse = Assert.IsType<BrowseResult>(ok.Value);
        Assert.True(browse.Directories.Count > 0, "Should return at least one drive root");
        Assert.Equal("", browse.CurrentPath);
    }

    [Fact]
    public void Browse_NonexistentPath_Returns404()
    {
        var result = _controller.Browse("/nonexistent/path/that/does/not/exist");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public void Browse_ValidDirectory_ReturnsDirectories()
    {
        // Use temp directory which always exists
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = _controller.Browse(tempDir);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var browse = Assert.IsType<BrowseResult>(ok.Value);
        Assert.Equal(Path.GetFullPath(tempDir), browse.CurrentPath);
    }

    [Fact]
    public void Browse_ValidDirectory_ReturnsParentPath()
    {
        var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = _controller.Browse(tempDir);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var browse = Assert.IsType<BrowseResult>(ok.Value);
        Assert.NotNull(browse.ParentPath);
    }

    [Fact]
    public void ListWorkflows_ReturnsExpectedWorkflows()
    {
        var result = _controller.ListWorkflows();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var workflows = Assert.IsType<List<WorkflowOption>>(ok.Value);
        Assert.True(workflows.Count >= 10, "Should have at least 10 workflow options");
    }

    [Fact]
    public void ListWorkflows_HasOneDefault()
    {
        var result = _controller.ListWorkflows();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var workflows = Assert.IsType<List<WorkflowOption>>(ok.Value);
        Assert.Single(workflows, w => w.IsDefault);
    }

    [Fact]
    public void ListWorkflows_DefaultIsFullOrchestrate()
    {
        var result = _controller.ListWorkflows();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var workflows = Assert.IsType<List<WorkflowOption>>(ok.Value);
        var defaultWorkflow = workflows.Single(w => w.IsDefault);
        Assert.Equal("full-orchestrate", defaultWorkflow.Id);
    }
}
