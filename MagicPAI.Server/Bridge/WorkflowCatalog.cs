// MagicPAI.Server/Bridge/WorkflowCatalog.cs
// Temporal-side workflow catalog per temporal.md §M.2. Holds the metadata
// SessionLaunchPlanner + SessionController need to dispatch any of the 16
// Temporal workflows. Elsa's version of this lives in ElsaWorkflowCatalog.cs
// until Phase 3.
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Bridge;

public record WorkflowCatalogEntry(
    string DisplayName,
    string WorkflowTypeName,
    string TaskQueue,
    Type InputType,
    string Description,
    bool RequiresAiAssistant,
    string[] SupportedModels,
    string Category,
    int SortOrder);

public class WorkflowCatalog
{
    public const string DefaultTaskQueue = "magicpai-main";

    public IReadOnlyList<WorkflowCatalogEntry> Entries { get; }

    public WorkflowCatalog()
    {
        Entries = new List<WorkflowCatalogEntry>
        {
            new(
                DisplayName: "Simple Agent",
                WorkflowTypeName: "SimpleAgent",
                TaskQueue: DefaultTaskQueue,
                InputType: typeof(SimpleAgentInput),
                Description: "Run a single AI agent task with verification and coverage loop.",
                RequiresAiAssistant: true,
                SupportedModels: AllModels,
                Category: "Core",
                SortOrder: 10),

            new(
                DisplayName: "Full Orchestrate",
                WorkflowTypeName: "FullOrchestrate",
                TaskQueue: DefaultTaskQueue,
                InputType: typeof(FullOrchestrateInput),
                Description: "Complete pipeline: website classification, research, triage, simple/complex path, verification.",
                RequiresAiAssistant: true,
                SupportedModels: AllModels,
                Category: "Core",
                SortOrder: 20),

            new(
                DisplayName: "Deep Research Orchestrate",
                WorkflowTypeName: "DeepResearchOrchestrate",
                TaskQueue: DefaultTaskQueue,
                InputType: typeof(DeepResearchOrchestrateInput),
                Description: "Research-first orchestration with deep codebase analysis.",
                RequiresAiAssistant: true,
                SupportedModels: AllModels,
                Category: "Core",
                SortOrder: 30),

            new("Orchestrate Simple Path", "OrchestrateSimplePath", DefaultTaskQueue,
                typeof(OrchestrateSimpleInput),
                "Route to simple agent path (no decomposition).", true, AllModels, "Paths", 40),

            new("Orchestrate Complex Path", "OrchestrateComplexPath", DefaultTaskQueue,
                typeof(OrchestrateComplexInput),
                "Decompose prompt and dispatch parallel child workflows.", true, AllModels, "Paths", 50),

            new("Standard Orchestrate", "StandardOrchestrate", DefaultTaskQueue,
                typeof(StandardOrchestrateInput),
                "Prompt enhance → agent run → verify/repair.", true, AllModels, "Paths", 60),

            new("Verify and Repair", "VerifyAndRepair", DefaultTaskQueue,
                typeof(VerifyAndRepairInput),
                "Reusable verification + repair loop (child workflow).", true, AllModels, "Utilities", 100),

            new("Prompt Enhancer", "PromptEnhancer", DefaultTaskQueue,
                typeof(PromptEnhancerInput),
                "Enhance a prompt for clarity and completeness.", true, AllModels, "Utilities", 110),

            new("Context Gatherer", "ContextGatherer", DefaultTaskQueue,
                typeof(ContextGathererInput),
                "Gather codebase context for a prompt.", true, AllModels, "Utilities", 120),

            new("Prompt Grounding", "PromptGrounding", DefaultTaskQueue,
                typeof(PromptGroundingInput),
                "Ground a prompt in the repository context.", true, AllModels, "Utilities", 130),

            new("Research Pipeline", "ResearchPipeline", DefaultTaskQueue,
                typeof(ResearchPipelineInput),
                "Deep research for a prompt.", true, AllModels, "Utilities", 140),

            new("Post Execution Pipeline", "PostExecutionPipeline", DefaultTaskQueue,
                typeof(PostExecInput),
                "Final verification + summary report.", true, AllModels, "Utilities", 150),

            new("Website Audit Core", "WebsiteAuditCore", DefaultTaskQueue,
                typeof(WebsiteAuditCoreInput),
                "Audit one website section (child workflow).", true, AllModels, "Website", 200),

            new("Website Audit Loop", "WebsiteAuditLoop", DefaultTaskQueue,
                typeof(WebsiteAuditInput),
                "Audit multiple website sections sequentially.", true, AllModels, "Website", 210),

            new("Claw Eval Agent", "ClawEvalAgent", DefaultTaskQueue,
                typeof(ClawEvalAgentInput),
                "Specialized workflow for claw/evaluation benchmarks.", true, AllModels, "Evaluation", 300),

            new("Complex Task Worker", "ComplexTaskWorker", DefaultTaskQueue,
                typeof(ComplexTaskInput),
                "Child workflow: executes one decomposed task.", true, AllModels, "Internal", 900),

        }.OrderBy(e => e.SortOrder).ToList();
    }

    public WorkflowCatalogEntry? Find(string workflowTypeName) =>
        Entries.FirstOrDefault(e =>
            e.WorkflowTypeName.Equals(workflowTypeName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<WorkflowCatalogEntry> ByCategory(string category) =>
        Entries.Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<WorkflowCatalogEntry> UserVisible =>
        Entries.Where(e => !e.Category.Equals("Internal", StringComparison.OrdinalIgnoreCase)).ToList();

    private static readonly string[] AllModels =
    {
        "auto", "sonnet", "opus", "haiku",
        "gpt-5.4", "gpt-5.3-codex",
        "gemini-3.1-pro-preview", "gemini-3-flash"
    };
}
