using MagicPAI.Core.Config;

namespace MagicPAI.Tests.Services;

public class ConfigValidationTests
{
    [Fact]
    public void DefaultConfig_IsValid()
    {
        var config = new MagicPaiConfig();
        var errors = config.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void InvalidConcurrentContainers_ReturnsError()
    {
        var config = new MagicPaiConfig { MaxConcurrentContainers = 0 };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("MaxConcurrentContainers"));
    }

    [Fact]
    public void InvalidCoverageThreshold_ReturnsError()
    {
        var config = new MagicPaiConfig { CoverageThreshold = 150 };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("CoverageThreshold"));
    }

    [Fact]
    public void NegativeBudget_ReturnsError()
    {
        var config = new MagicPaiConfig { MaxBudgetUsd = -5 };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("MaxBudgetUsd"));
    }

    [Fact]
    public void InvalidPortRange_ReturnsError()
    {
        var config = new MagicPaiConfig { GuiPortRangeStart = 100 };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("GuiPortRangeStart"));
    }

    [Fact]
    public void InvalidComplexityThreshold_ReturnsError()
    {
        var config = new MagicPaiConfig { ComplexityThreshold = 0 };
        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("ComplexityThreshold"));
    }

    [Fact]
    public void MultipleErrors_AllReturned()
    {
        var config = new MagicPaiConfig
        {
            MaxConcurrentContainers = 0,
            ContainerTimeoutMinutes = 0,
            MaxBudgetUsd = -1
        };
        var errors = config.Validate();
        Assert.True(errors.Count >= 3);
    }

    [Fact]
    public void ContainerizedAgents_Require_Docker()
    {
        var config = new MagicPaiConfig
        {
            UseDocker = false,
            RequireContainerizedAgentExecution = true
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("RequireContainerizedAgentExecution"));
    }
}
