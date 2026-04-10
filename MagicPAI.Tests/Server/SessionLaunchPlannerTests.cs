using MagicPAI.Core.Config;
using MagicPAI.Server.Services;

namespace MagicPAI.Tests.Server;

public class SessionLaunchPlannerTests
{
    [Fact]
    public void Plan_FullOrchestrate_RequiresDockerWorkerExecution()
    {
        var planner = new SessionLaunchPlanner(new MagicPaiConfig
        {
            UseDocker = false,
            ExecutionBackend = "docker",
            DefaultAgent = "claude"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => planner.Plan(
            prompt: "Fix backend bug",
            workspacePath: "C:/repo",
            aiAssistant: "claude",
            agent: "claude",
            model: "auto",
            modelPower: 2,
            structuredOutputSchema: null,
            workflowName: "full-orchestrate"));

        Assert.Contains("requires Docker worker execution", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_FullOrchestrate_EnablesGui_And_ResolvesDefinition()
    {
        var planner = new SessionLaunchPlanner(new MagicPaiConfig
        {
            UseDocker = true,
            ExecutionBackend = "docker",
            DefaultAgent = "claude"
        });

        var plan = planner.Plan(
            prompt: "Audit the website",
            workspacePath: "C:/repo",
            aiAssistant: "claude",
            agent: "claude",
            model: "auto",
            modelPower: 2,
            structuredOutputSchema: null,
            workflowName: "full-orchestrate");

        Assert.Equal("FullOrchestrateWorkflow", plan.DefinitionId);
        Assert.True(plan.EnableGui);
        Assert.Equal("claude", plan.ResolvedAssistant);
        Assert.True((bool)plan.Input["EnableGui"]);
    }

    [Fact]
    public void Plan_StandardWorkflow_DoesNotForceGui()
    {
        var planner = new SessionLaunchPlanner(new MagicPaiConfig
        {
            UseDocker = false,
            ExecutionBackend = "docker",
            DefaultAgent = "claude"
        });

        var plan = planner.Plan(
            prompt: "Improve docs",
            workspacePath: "C:/repo",
            aiAssistant: "claude",
            agent: "claude",
            model: "auto",
            modelPower: 2,
            structuredOutputSchema: null,
            workflowName: "standard-orchestrate");

        Assert.Equal("StandardOrchestrateWorkflow", plan.DefinitionId);
        Assert.False(plan.EnableGui);
        Assert.False((bool)plan.Input["EnableGui"]);
    }
}
