// MagicPAI.Server/Services/SessionLaunchPlanner.cs
// Temporal-based session launch planner per temporal.md §M.3. Converts the
// generic CreateSessionRequest into a strongly-typed workflow input record
// per workflow type. The Elsa-era shape (PlannedSessionLaunch with
// Dictionary<string,object> input) is gone — callers now dispatch via
// SessionController's typed switch.
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Controllers;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Services;

public sealed record SessionLaunchPlan(
    string WorkflowType,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string WorkspacePath,
    bool EnableGui,
    string SessionKind,
    CreateSessionRequest OriginalRequest);

public class SessionLaunchPlanner
{
    private readonly WorkflowCatalog _catalog;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<SessionLaunchPlanner>? _log;

    private static readonly HashSet<string> GuiDefaultWorkflows = new(StringComparer.OrdinalIgnoreCase)
    {
        "FullOrchestrate",
        "DeepResearchOrchestrate",
        "StandardOrchestrate",
        "WebsiteAuditLoop"
    };

    public SessionLaunchPlanner(
        WorkflowCatalog catalog,
        MagicPaiConfig config,
        ILogger<SessionLaunchPlanner>? log = null)
    {
        _catalog = catalog;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Build a launch plan for the given request. Validates the workflow name,
    /// resolves the AI assistant against defaults, and normalizes the model
    /// string ("auto" → null so Temporal activities pick the backend default).
    /// </summary>
    public SessionLaunchPlan Plan(CreateSessionRequest req)
    {
        var entry = _catalog.Find(req.WorkflowType)
            ?? throw new ArgumentException($"Unknown workflow type: {req.WorkflowType}");

        var assistant = AiAssistantResolver.NormalizeAssistant(req.AiAssistant, _config.DefaultAgent);
        var model = NormalizeModel(req.Model);
        var enableGui = req.EnableGui ?? GuiDefaultWorkflows.Contains(entry.WorkflowTypeName);
        var workspace = string.IsNullOrWhiteSpace(req.WorkspacePath)
            ? (string.IsNullOrWhiteSpace(_config.WorkspacePath) ? "/workspace" : _config.WorkspacePath)
            : req.WorkspacePath;
        var sessionKind = DetermineSessionKind(entry.WorkflowTypeName);

        _log?.LogDebug(
            "Planned session: wf={Workflow}, assistant={Assistant}, model={Model}, gui={Gui}, kind={Kind}",
            entry.WorkflowTypeName, assistant, model ?? "auto", enableGui, sessionKind);

        return new SessionLaunchPlan(
            WorkflowType: entry.WorkflowTypeName,
            AiAssistant: assistant,
            Model: model,
            ModelPower: req.ModelPower,
            WorkspacePath: workspace,
            EnableGui: enableGui,
            SessionKind: sessionKind,
            OriginalRequest: req);
    }

    private static string? NormalizeModel(string? model) =>
        string.IsNullOrWhiteSpace(model) || model.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : model;

    private static string DetermineSessionKind(string wfType) => wfType switch
    {
        "SimpleAgent" or "OrchestrateSimplePath" => "simple",
        "FullOrchestrate" or "DeepResearchOrchestrate" or "StandardOrchestrate" => "full",
        "OrchestrateComplexPath" or "ComplexTaskWorker" => "complex",
        "WebsiteAuditCore" or "WebsiteAuditLoop" => "website",
        "VerifyAndRepair" or "PostExecutionPipeline" or "IterativeLoop" => "utility",
        "PromptEnhancer" or "ContextGatherer" or "PromptGrounding" or "ResearchPipeline" => "prompt-tooling",
        "ClawEvalAgent" => "evaluation",
        _ => "unknown"
    };

    // ───────────────────────────────────────────────────────────────────────
    // Typed converters — one per workflow input record. Callers (controller /
    // hub) use these to build the concrete input object for StartWorkflowAsync.
    // ───────────────────────────────────────────────────────────────────────

    public SimpleAgentInput AsSimpleAgentInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            WorkspacePath: plan.WorkspacePath,
            EnableGui: plan.EnableGui,
            SkipCoverageWhenGatesPass: plan.OriginalRequest.SkipCoverageWhenGatesPass ?? false);

    public FullOrchestrateInput AsFullOrchestrateInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            EnableGui: plan.EnableGui,
            RequireTriageApproval: plan.OriginalRequest.RequireTriageApproval ?? false,
            GateApprovalTimeoutHours: plan.OriginalRequest.GateApprovalTimeoutHours ?? 24,
            ComplexityThreshold: plan.OriginalRequest.ComplexityThreshold ?? 3);

    public OrchestrateSimpleInput AsOrchestrateSimplePathInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            EnableGui: plan.EnableGui);

    public OrchestrateComplexInput AsOrchestrateComplexPathInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower);

    public PromptEnhancerInput AsPromptEnhancerInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            OriginalPrompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            AiAssistant: plan.AiAssistant,
            ModelPower: plan.ModelPower,
            WorkspacePath: plan.WorkspacePath);

    public ContextGathererInput AsContextGathererInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkingDirectory: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant);

    public PromptGroundingInput AsPromptGroundingInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkingDirectory: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant);

    public ResearchPipelineInput AsResearchPipelineInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkingDirectory: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant);

    public PostExecInput AsPostExecInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            ContainerId: "",
            WorkingDirectory: plan.WorkspacePath,
            AgentResponse: plan.OriginalRequest.Prompt,
            AiAssistant: plan.AiAssistant);

    public StandardOrchestrateInput AsStandardInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            EnableGui: plan.EnableGui);

    public ClawEvalAgentInput AsClawEvalInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            EvalTaskId: plan.OriginalRequest.EvalTaskId ?? workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower);

    public WebsiteAuditCoreInput AsWebsiteCoreInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            SectionId: plan.OriginalRequest.SectionId ?? "default",
            SectionDescription: plan.OriginalRequest.Prompt,
            ContainerId: "",
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model);

    public WebsiteAuditInput AsWebsiteLoopInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            ContainerId: "",
            Prompt: plan.OriginalRequest.Prompt,
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            SectionIds: plan.OriginalRequest.SectionIds);

    public VerifyAndRepairInput AsVerifyRepairInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            ContainerId: plan.OriginalRequest.ContainerId ?? "",
            WorkingDirectory: plan.WorkspacePath,
            OriginalPrompt: plan.OriginalRequest.Prompt,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            Gates: plan.OriginalRequest.Gates ?? new[] { "compile", "test" },
            WorkerOutput: plan.OriginalRequest.WorkerOutput ?? "",
            MaxRepairAttempts: plan.OriginalRequest.MaxRepairAttempts ?? 3);

    public DeepResearchOrchestrateInput AsDeepResearchInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            SessionId: workflowId,
            Prompt: plan.OriginalRequest.Prompt,
            WorkspacePath: plan.WorkspacePath,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            EnableGui: plan.EnableGui);

    public ComplexTaskInput AsComplexTaskWorkerInput(SessionLaunchPlan plan, string workflowId) =>
        new(
            TaskId: plan.OriginalRequest.TaskId ?? workflowId,
            Description: plan.OriginalRequest.Prompt,
            DependsOn: plan.OriginalRequest.DependsOn ?? Array.Empty<string>(),
            FilesTouched: plan.OriginalRequest.FilesTouched ?? Array.Empty<string>(),
            ContainerId: plan.OriginalRequest.ContainerId ?? "",
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            WorkspacePath: plan.WorkspacePath,
            ParentSessionId: plan.OriginalRequest.ParentSessionId ?? workflowId);

    public IterativeLoopInput AsIterativeLoopInput(SessionLaunchPlan plan, string workflowId)
    {
        var req = plan.OriginalRequest;
        return new IterativeLoopInput(
            SessionId: workflowId,
            Prompt: req.Prompt,
            AiAssistant: plan.AiAssistant,
            Model: plan.Model,
            ModelPower: plan.ModelPower,
            MinIterations: req.MinIterations ?? 1,
            MaxIterations: req.MaxIterations ?? 10,
            CompletionStrategy: ParseStrategy(req.CompletionStrategy),
            CompletionMarker: string.IsNullOrWhiteSpace(req.CompletionMarker) ? "[DONE]" : req.CompletionMarker!,
            CompletionInstructions: req.CompletionInstructions,
            WorkspacePath: plan.WorkspacePath,
            ExistingContainerId: req.ContainerId,
            EnableGui: plan.EnableGui,
            MaxBudgetUsd: req.MaxBudgetUsd ?? 0m);
    }

    private static CompletionStrategy ParseStrategy(string? s) =>
        s?.ToLowerInvariant() switch
        {
            "marker" => CompletionStrategy.Marker,
            "classifier" => CompletionStrategy.Classifier,
            "structured" or "structuredprogress" or null or "" => CompletionStrategy.StructuredProgress,
            _ => CompletionStrategy.StructuredProgress,
        };
}
