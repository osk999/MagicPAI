using MagicPAI.Core.Config;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Controllers;
using MagicPAI.Server.Services;

namespace MagicPAI.Tests.Server;

[Trait("Category", "Unit")]
public class SessionLaunchPlannerTests
{
    [Fact]
    public void Plan_UnknownWorkflowType_Throws()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(Prompt: "hi", WorkflowType: "NonExistent");
        Assert.Throws<ArgumentException>(() => planner.Plan(req));
    }

    [Fact]
    public void Plan_SimpleAgent_FillsPlanDefaults()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "do the thing",
            WorkflowType: "SimpleAgent",
            AiAssistant: "claude",
            Model: "auto",
            ModelPower: 2,
            WorkspacePath: "/workspace");

        var plan = planner.Plan(req);

        Assert.Equal("SimpleAgent", plan.WorkflowType);
        Assert.Equal("claude", plan.AiAssistant);
        Assert.Null(plan.Model); // "auto" normalizes to null
        Assert.Equal(2, plan.ModelPower);
        Assert.Equal("/workspace", plan.WorkspacePath);
        Assert.Equal("simple", plan.SessionKind);
    }

    [Fact]
    public void Plan_FullOrchestrate_EnablesGuiByDefault()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "Audit the website",
            WorkflowType: "FullOrchestrate",
            AiAssistant: "claude");

        var plan = planner.Plan(req);

        Assert.Equal("FullOrchestrate", plan.WorkflowType);
        Assert.True(plan.EnableGui);
        Assert.Equal("full", plan.SessionKind);
    }

    [Fact]
    public void Plan_SimpleAgent_DoesNotEnableGuiByDefault()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "small refactor",
            WorkflowType: "SimpleAgent",
            AiAssistant: "claude");

        var plan = planner.Plan(req);

        Assert.False(plan.EnableGui);
    }

    [Fact]
    public void Plan_EnableGuiTrue_ForcesGuiOn()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "x",
            WorkflowType: "SimpleAgent",
            AiAssistant: "claude",
            EnableGui: true);

        var plan = planner.Plan(req);

        Assert.True(plan.EnableGui);
    }

    [Fact]
    public void AsSimpleAgentInput_BuildsCanonicalRecord()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "compile this",
            WorkflowType: "SimpleAgent",
            AiAssistant: "codex",
            Model: "sonnet",
            ModelPower: 3,
            WorkspacePath: "C:/repo");

        var plan = planner.Plan(req);
        var input = planner.AsSimpleAgentInput(plan, workflowId: "mpai-test");

        Assert.Equal("mpai-test", input.SessionId);
        Assert.Equal("compile this", input.Prompt);
        Assert.Equal("codex", input.AiAssistant);
        Assert.Equal("sonnet", input.Model);
        Assert.Equal(3, input.ModelPower);
        Assert.Equal("C:/repo", input.WorkspacePath);
    }

    [Fact]
    public void Plan_SmartImprove_HasSmartImproveKind()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "Improve the project end-to-end.",
            WorkflowType: "SmartImprove",
            AiAssistant: "claude",
            WorkspacePath: "C:/tmp/work");

        var plan = planner.Plan(req);

        Assert.Equal("SmartImprove", plan.WorkflowType);
        Assert.Equal("smart-improve", plan.SessionKind);
        Assert.Equal("C:/tmp/work", plan.WorkspacePath);
    }

    [Fact]
    public void AsSmartImproveInput_PassesThroughOverrides()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "Improve.",
            WorkflowType: "SmartImprove",
            AiAssistant: "claude",
            WorkspacePath: "C:/tmp/work",
            BurstSchedule: new[] { 4, 4, 2 },
            SteadyStateBurstSize: 2,
            MaxTotalIterations: 50,
            MaxTotalBudgetUsd: 5m,
            MaxBursts: 6,
            RequiredCleanVerifies: 1,
            SilenceCountdownIterations: 3);
        var plan = planner.Plan(req);

        var input = planner.AsSmartImproveInput(plan, "wf-smart-1");

        Assert.Equal("wf-smart-1", input.SessionId);
        Assert.Equal("Improve.", input.Prompt);
        Assert.Equal("claude", input.AiAssistant);
        Assert.Equal("C:/tmp/work", input.WorkspacePath);
        Assert.Equal(new[] { 4, 4, 2 }, input.BurstSchedule);
        Assert.Equal(2, input.SteadyStateBurstSize);
        Assert.Equal(50, input.MaxTotalIterations);
        Assert.Equal(5m, input.MaxTotalBudgetUsd);
        Assert.Equal(6, input.MaxBursts);
        Assert.Equal(1, input.RequiredCleanVerifies);
        Assert.Equal(3, input.SilenceCountdownIterations);
    }

    [Fact]
    public void AsSmartImproveInput_AppliesDefaultsWhenOmitted()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "Improve.",
            WorkflowType: "SmartImprove",
            AiAssistant: "claude");
        var plan = planner.Plan(req);

        var input = planner.AsSmartImproveInput(plan, "wf-defaults");

        // Defaults from newplan.md §2.3 — keep these aligned with the workflow
        // Input record's default values.
        Assert.Equal(5, input.SteadyStateBurstSize);
        Assert.Equal(200, input.MaxTotalIterations);
        Assert.Equal(50m, input.MaxTotalBudgetUsd);
        Assert.Equal(30, input.MaxBursts);
        Assert.Equal(2, input.RequiredCleanVerifies);
        Assert.Equal(2, input.SilenceCountdownIterations);
        Assert.Null(input.BurstSchedule); // null → workflow uses [8,8,5,5,...]
    }

    [Fact]
    public void Plan_PromptEnhancer_UtilityKind()
    {
        var planner = BuildPlanner();
        var req = new CreateSessionRequest(
            Prompt: "enhance me",
            WorkflowType: "PromptEnhancer",
            AiAssistant: "claude");

        var plan = planner.Plan(req);

        Assert.Equal("PromptEnhancer", plan.WorkflowType);
        Assert.Equal("prompt-tooling", plan.SessionKind);
    }

    private static SessionLaunchPlanner BuildPlanner(MagicPaiConfig? config = null) =>
        new(new WorkflowCatalog(), config ?? new MagicPaiConfig
        {
            UseDocker = true,
            ExecutionBackend = "docker",
            DefaultAgent = "claude",
            WorkspacePath = "/workspace"
        });
}
