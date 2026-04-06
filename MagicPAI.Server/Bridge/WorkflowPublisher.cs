using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using MagicPAI.Server.Workflows;

namespace MagicPAI.Server.Bridge;

public class WorkflowPublisher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowPublisher> _logger;

    // All our code-first workflow types
    public static readonly Type[] WorkflowTypes =
    [
        typeof(FullOrchestrateWorkflow),
        typeof(SimpleAgentWorkflow),
        typeof(VerifyAndRepairWorkflow),
        typeof(PromptEnhancerWorkflow),
        typeof(ContextGathererWorkflow),
        typeof(PromptGroundingWorkflow),
        typeof(LoopVerifierWorkflow),
        typeof(WebsiteAuditLoopWorkflow),
        typeof(IsComplexAppWorkflow),
        typeof(IsWebsiteProjectWorkflow),
        typeof(OrchestrateComplexPathWorkflow),
        typeof(OrchestrateSimplePathWorkflow),
        typeof(PostExecutionPipelineWorkflow),
        typeof(ResearchPipelineWorkflow),
        typeof(StandardOrchestrateWorkflow),
        typeof(TestSetPromptWorkflow),
        typeof(ClawEvalAgentWorkflow),
    ];

    // Map friendly names (e.g., "simple-agent") -> DefinitionId (e.g., "SimpleAgentWorkflow")
    private static readonly Dictionary<string, string> FriendlyNameMap = WorkflowTypes
        .ToDictionary(
            t => ToFriendlyName(t.Name),
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a workflow name to the DefinitionVersionId used for dispatch.
    /// Returns the versioned ID (e.g., "SimpleAgentWorkflow:v1") for code-first workflows,
    /// or the raw name for designer-created workflows.
    /// </summary>
    public static string ResolveDefinitionVersionId(string workflowName)
    {
        // Check if it's a code-first workflow by class name
        if (WorkflowTypes.Any(t => t.Name.Equals(workflowName, StringComparison.OrdinalIgnoreCase)))
            return $"{workflowName}:v1";

        // Check friendly name (e.g., "simple-agent" → "SimpleAgentWorkflow")
        if (FriendlyNameMap.TryGetValue(workflowName, out var defId))
            return $"{defId}:v1";

        // Unknown workflow — return as-is (may be designer-created)
        return workflowName;
    }

    /// <summary>Resolve to just the DefinitionId (without version suffix).</summary>
    public static string ResolveDefinitionId(string workflowName)
    {
        if (WorkflowTypes.Any(t => t.Name.Equals(workflowName, StringComparison.OrdinalIgnoreCase)))
            return workflowName;
        if (FriendlyNameMap.TryGetValue(workflowName, out var defId))
            return defId;
        return workflowName;
    }

    private static string ToFriendlyName(string className)
    {
        var name = className.Replace("Workflow", "");
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "-" + char.ToLower(c) : char.ToLower(c).ToString()));
    }

    public WorkflowPublisher(IServiceProvider services, ILogger<WorkflowPublisher> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(5000, ct);
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            var store = sp.GetRequiredService<IWorkflowDefinitionStore>();

            var count = 0;
            foreach (var type in WorkflowTypes)
            {
                try
                {
                    var defId = type.Name;
                    var filter = new WorkflowDefinitionFilter { DefinitionId = defId };
                    var existing = await store.FindManyAsync(filter, ct);
                    if (existing.Any())
                    {
                        _logger.LogDebug("Already published: {Name}", defId);
                        continue;
                    }

                    // Create a stub definition that uses ClrWorkflowMaterializer.
                    // This tells Elsa to rebuild the workflow from the C# code at runtime,
                    // keeping the flowchart structure (activities + connections) intact.
                    var def = new WorkflowDefinition
                    {
                        Id = $"{defId}:v1",
                        DefinitionId = defId,
                        Name = defId,
                        Version = 1,
                        IsLatest = true,
                        IsPublished = true,
                        IsSystem = false,
                        IsReadonly = true,
                        MaterializerName = "ClrWorkflowMaterializer",
                        MaterializerContext = System.Text.Json.JsonSerializer.Serialize(
                            new { TypeName = type.AssemblyQualifiedName }),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };

                    await store.SaveAsync(def, ct);
                    count++;
                    _logger.LogInformation("Published: {Name} (CLR materializer)", defId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed: {Type}", type.Name);
                }
            }
            _logger.LogInformation("Published {Count} new workflows ({Total} total types)", count, WorkflowTypes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow publish failed");
        }
    }
}
