using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;

namespace MagicPAI.Server.Services;

public class SessionLaunchPlanner
{
    private static readonly HashSet<string> DockerOnlyWorkflows = new(StringComparer.OrdinalIgnoreCase)
    {
        "full-orchestrate",
        nameof(Workflows.FullOrchestrateWorkflow),
        "website-audit-loop",
        nameof(Workflows.WebsiteAuditLoopWorkflow)
    };

    private static readonly HashSet<string> GuiWorkflows = new(StringComparer.OrdinalIgnoreCase)
    {
        "full-orchestrate",
        nameof(Workflows.FullOrchestrateWorkflow),
        "website-audit-loop",
        nameof(Workflows.WebsiteAuditLoopWorkflow)
    };

    private readonly MagicPaiConfig _config;

    public SessionLaunchPlanner(MagicPaiConfig config)
    {
        _config = config;
    }

    public PlannedSessionLaunch Plan(
        string prompt,
        string? workspacePath,
        string? aiAssistant,
        string? agent,
        string? model,
        int modelPower,
        string? structuredOutputSchema,
        string workflowName)
    {
        var requestedWorkflow = string.IsNullOrWhiteSpace(workflowName) ? "full-orchestrate" : workflowName;
        var resolvedAssistant = AiAssistantResolver.NormalizeAssistant(aiAssistant ?? agent, _config.DefaultAgent);
        var requiresDocker = DockerOnlyWorkflows.Contains(requestedWorkflow);
        var enableGui = GuiWorkflows.Contains(requestedWorkflow);

        if (requiresDocker && (!_config.UseDocker || string.Equals(_config.ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Workflow '{requestedWorkflow}' requires Docker worker execution with GUI support. Local and Kubernetes execution are not allowed for this workflow.");

        return new PlannedSessionLaunch(
            RequestedWorkflowName: requestedWorkflow,
            DefinitionId: WorkflowPublisher.ResolveDefinitionId(requestedWorkflow),
            ResolvedAssistant: resolvedAssistant,
            EnableGui: enableGui,
            Input: new Dictionary<string, object>
            {
                ["Prompt"] = prompt,
                ["WorkspacePath"] = workspacePath ?? "",
                ["AiAssistant"] = resolvedAssistant,
                ["Agent"] = resolvedAssistant,
                ["Model"] = string.IsNullOrWhiteSpace(model) ? "auto" : model,
                ["ModelPower"] = modelPower,
                ["StructuredOutputSchema"] = structuredOutputSchema ?? "",
                ["EnableGui"] = enableGui
            });
    }
}

public sealed record PlannedSessionLaunch(
    string RequestedWorkflowName,
    string DefinitionId,
    string ResolvedAssistant,
    bool EnableGui,
    Dictionary<string, object> Input);
