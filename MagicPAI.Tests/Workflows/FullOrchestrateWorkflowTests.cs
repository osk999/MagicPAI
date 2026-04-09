using MagicPAI.Server.Workflows;
using WorkflowBase = MagicPAI.Server.Workflows.WorkflowBase;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Tests for all 17 workflow types — verifying they exist, are properly
/// subclassed, and can be instantiated without errors.
/// </summary>
public class FullOrchestrateWorkflowTests
{
    // All 17 workflow types
    public static IEnumerable<object[]> AllWorkflowTypes =>
    [
        [typeof(FullOrchestrateWorkflow)],
        [typeof(SimpleAgentWorkflow)],
        [typeof(VerifyAndRepairWorkflow)],
        [typeof(PromptEnhancerWorkflow)],
        [typeof(ContextGathererWorkflow)],
        [typeof(PromptGroundingWorkflow)],
        [typeof(LoopVerifierWorkflow)],
        [typeof(WebsiteAuditLoopWorkflow)],
        [typeof(IsComplexAppWorkflow)],
        [typeof(IsWebsiteProjectWorkflow)],
        [typeof(OrchestrateComplexPathWorkflow)],
        [typeof(OrchestrateSimplePathWorkflow)],
        [typeof(PostExecutionPipelineWorkflow)],
        [typeof(ResearchPipelineWorkflow)],
        [typeof(StandardOrchestrateWorkflow)],
        [typeof(TestSetPromptWorkflow)],
        [typeof(ClawEvalAgentWorkflow)],
    ];

    [Theory]
    [MemberData(nameof(AllWorkflowTypes))]
    public void Workflow_Extends_WorkflowBase(Type workflowType)
    {
        Assert.True(typeof(WorkflowBase).IsAssignableFrom(workflowType),
            $"{workflowType.Name} should extend WorkflowBase");
    }

    [Theory]
    [MemberData(nameof(AllWorkflowTypes))]
    public void Workflow_IsInstantiable(Type workflowType)
    {
        var instance = Activator.CreateInstance(workflowType);
        Assert.NotNull(instance);
    }

    [Theory]
    [MemberData(nameof(AllWorkflowTypes))]
    public void Workflow_IsInCorrectNamespace(Type workflowType)
    {
        Assert.Equal("MagicPAI.Server.Workflows", workflowType.Namespace);
    }

    [Theory]
    [MemberData(nameof(AllWorkflowTypes))]
    public void Workflow_IsPublic(Type workflowType)
    {
        Assert.True(workflowType.IsPublic, $"{workflowType.Name} should be public");
    }

    [Theory]
    [MemberData(nameof(AllWorkflowTypes))]
    public void Workflow_HasBuildMethod(Type workflowType)
    {
        var buildMethod = workflowType.GetMethod("Build",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(buildMethod);
    }

    [Fact]
    public void AllWorkflows_Count_Is_17()
    {
        // Ensure we haven't missed any workflow types
        var workflowTypes = typeof(FullOrchestrateWorkflow).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(WorkflowBase).IsAssignableFrom(t))
            .ToList();

        Assert.Equal(17, workflowTypes.Count);
    }

    [Fact]
    public void AllWorkflows_HaveUniqueNames()
    {
        var workflowTypes = typeof(FullOrchestrateWorkflow).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(WorkflowBase).IsAssignableFrom(t))
            .ToList();

        var names = workflowTypes.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
