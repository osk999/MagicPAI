using Elsa.Workflows;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Server.Bridge;

public sealed record WorkflowCatalogEntry(
    string DefinitionId,
    string FriendlyName,
    string TemplateFileName,
    Type WorkflowType,
    bool UseJsonTemplate);

public static class WorkflowCatalog
{
    public static IReadOnlyList<WorkflowCatalogEntry> Entries { get; } =
    [
        Create<SimpleAgentWorkflow>(),
        Create<VerifyAndRepairWorkflow>(),
        Create<PromptEnhancerWorkflow>(),
        Create<ContextGathererWorkflow>(),
        Create<PromptGroundingWorkflow>(),
        Create<IsComplexAppWorkflow>(),
        Create<IsWebsiteProjectWorkflow>(),
        Create<OrchestrateComplexPathWorkflow>(),
        Create<OrchestrateSimplePathWorkflow>(),
        Create<PostExecutionPipelineWorkflow>(),
        Create<ResearchPipelineWorkflow>(),
        Create<StandardOrchestrateWorkflow>(),
        Create<TestSetPromptWorkflow>(),
        Create<ClawEvalAgentWorkflow>(),
        Create<LoopVerifierWorkflow>(),
        Create<WebsiteAuditCoreWorkflow>(useJsonTemplate: false),
        Create<WebsiteAuditLoopWorkflow>(useJsonTemplate: false),
        Create<FullOrchestrateWorkflow>(useJsonTemplate: false),
        Create<TestClassifierWorkflow>(),
        Create<TestWebsiteClassifierWorkflow>(),
        Create<TestPromptEnhancementWorkflow>(),
        Create<TestFullFlowWorkflow>()
    ];

    public static string ResolveDefinitionId(string workflowName)
    {
        var entry = Entries.FirstOrDefault(x =>
            x.DefinitionId.Equals(workflowName, StringComparison.OrdinalIgnoreCase) ||
            x.FriendlyName.Equals(workflowName, StringComparison.OrdinalIgnoreCase));
        return entry?.DefinitionId ?? workflowName;
    }

    public static string ResolveDefinitionVersionId(string workflowName)
    {
        var definitionId = ResolveDefinitionId(workflowName);
        return $"{definitionId}:v1";
    }

    public static bool RequiresPublishedDefinitionDispatch(string workflowName)
    {
        var definitionId = ResolveDefinitionId(workflowName);
        var entry = Entries.FirstOrDefault(x =>
            x.DefinitionId.Equals(definitionId, StringComparison.OrdinalIgnoreCase));
        return entry?.UseJsonTemplate ?? true;
    }

    private static WorkflowCatalogEntry Create<TWorkflow>(bool useJsonTemplate = true) where TWorkflow : IWorkflow =>
        new(
            typeof(TWorkflow).Name,
            ToFriendlyName(typeof(TWorkflow).Name),
            $"{ToFriendlyName(typeof(TWorkflow).Name)}.json",
            typeof(TWorkflow),
            useJsonTemplate);

    private static string ToFriendlyName(string className)
    {
        var name = className.Replace("Workflow", "", StringComparison.Ordinal);
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
