using MagicPAI.Core.Models;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Gates;
using Moq;

namespace MagicPAI.Tests.Services;

public class HallucinationDetectorTests
{
    [Fact]
    public async Task VerifyAsync_Excludes_Generated_Output_Trees()
    {
        var container = new Mock<IContainerManager>();
        var commands = new List<string>();

        container.Setup(c => c.ExecAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, command, _, _) => commands.Add(command))
            .ReturnsAsync(new ExecResult(0, string.Empty, string.Empty));

        var gate = new HallucinationDetector();

        var result = await gate.VerifyAsync(container.Object, "container-1", "/workspace", CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command =>
        {
            Assert.Contains("-not -path './artifacts/*'", command);
            Assert.Contains("-not -path './publish/*'", command);
            Assert.Contains("-not -path '*/wwwroot/_content/*'", command);
            Assert.Contains("-not -path '*/wwwroot/_framework/*'", command);
        });
    }
}
