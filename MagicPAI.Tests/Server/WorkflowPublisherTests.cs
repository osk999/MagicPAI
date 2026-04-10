using MagicPAI.Server.Bridge;

namespace MagicPAI.Tests.Server;

public class WorkflowPublisherTests
{
    [Fact]
    public void Catalog_Contains_Template_For_Each_Workflow()
    {
        Assert.Contains(WorkflowCatalog.Entries, x => x.DefinitionId == "WebsiteAuditCoreWorkflow");
        Assert.All(WorkflowCatalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.FriendlyName));
            Assert.EndsWith(".json", entry.TemplateFileName);
        });
    }

    [Theory]
    [InlineData("FullOrchestrateWorkflow", "FullOrchestrateWorkflow")]
    [InlineData("SimpleAgentWorkflow", "SimpleAgentWorkflow")]
    [InlineData("VerifyAndRepairWorkflow", "VerifyAndRepairWorkflow")]
    [InlineData("ClawEvalAgentWorkflow", "ClawEvalAgentWorkflow")]
    public void ResolveDefinitionId_FromClassName_ReturnsClassName(string input, string expected)
    {
        var result = WorkflowPublisher.ResolveDefinitionId(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("full-orchestrate", "FullOrchestrateWorkflow")]
    [InlineData("simple-agent", "SimpleAgentWorkflow")]
    [InlineData("verify-and-repair", "VerifyAndRepairWorkflow")]
    [InlineData("context-gatherer", "ContextGathererWorkflow")]
    [InlineData("prompt-enhancer", "PromptEnhancerWorkflow")]
    [InlineData("prompt-grounding", "PromptGroundingWorkflow")]
    [InlineData("loop-verifier", "LoopVerifierWorkflow")]
    [InlineData("website-audit-core", "WebsiteAuditCoreWorkflow")]
    [InlineData("website-audit-loop", "WebsiteAuditLoopWorkflow")]
    [InlineData("claw-eval-agent", "ClawEvalAgentWorkflow")]
    public void ResolveDefinitionId_FromFriendlyName_ReturnsClassName(string input, string expected)
    {
        var result = WorkflowPublisher.ResolveDefinitionId(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveDefinitionId_UnknownName_ReturnsInput()
    {
        var result = WorkflowPublisher.ResolveDefinitionId("completely-unknown");
        Assert.Equal("completely-unknown", result);
    }

    [Theory]
    [InlineData("full-orchestrate")]
    [InlineData("FullOrchestrateWorkflow")]
    public void ResolveDefinitionId_IsCaseInsensitive(string input)
    {
        var result = WorkflowPublisher.ResolveDefinitionId(input);
        Assert.Equal("FullOrchestrateWorkflow", result);
    }

    [Theory]
    [InlineData("full-orchestrate", "FullOrchestrateWorkflow:v1")]
    [InlineData("FullOrchestrateWorkflow", "FullOrchestrateWorkflow:v1")]
    [InlineData("simple-agent", "SimpleAgentWorkflow:v1")]
    public void ResolveDefinitionVersionId_AppendsV1(string input, string expected)
    {
        var result = WorkflowPublisher.ResolveDefinitionVersionId(input);
        Assert.Equal(expected, result);
    }
}
