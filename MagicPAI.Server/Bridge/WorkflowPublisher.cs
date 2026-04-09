using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Activities;
using MagicPAI.Core.Config;
using Medallion.Threading;
using MagicPAI.Server.Workflows;
using System.Security.Cryptography;
using System.Text;

namespace MagicPAI.Server.Bridge;

public class WorkflowPublisher : BackgroundService
{
    private const string PublishLockName = "magicpai:workflow-publisher";
    private const string PublisherFormatVersion = "4";
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowPublisher> _logger;

    // All code-first workflow types
    private static readonly Type[] WorkflowTypes =
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

    // Map friendly names → class names
    private static readonly Dictionary<string, string> FriendlyNameMap = WorkflowTypes
        .ToDictionary(
            t => ToFriendlyName(t.Name),
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a friendly name to the DefinitionId (class name).</summary>
    public static string ResolveDefinitionId(string workflowName)
    {
        if (WorkflowTypes.Any(t => t.Name.Equals(workflowName, StringComparison.OrdinalIgnoreCase)))
            return workflowName;
        if (FriendlyNameMap.TryGetValue(workflowName, out var defId))
            return defId;
        return workflowName;
    }

    /// <summary>Resolve to DefinitionVersionId for dispatch.</summary>
    public static string ResolveDefinitionVersionId(string workflowName)
    {
        var defId = ResolveDefinitionId(workflowName);
        return $"{defId}:v1";
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
        // Wait for Elsa's migration startup tasks to complete before publishing.
        // On fresh databases, EF Core migrations need a few seconds.
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            // Patch: add OriginalSource column for Elsa 3.6 compatibility.
            // Elsa 3.5.3 EF Core migrations don't create it, but 3.6 queries expect it.
            await PatchSchemaAsync(sp, ct);

            var config = sp.GetRequiredService<MagicPaiConfig>();

            if (string.Equals(config.ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString("MagicPai");
                var lockProvider = PostgresDistributedLockFactory.Create(connectionString!);

                _logger.LogInformation("Acquiring distributed workflow publish lock");
                await using (await lockProvider.AcquireLockAsync(PublishLockName, null, ct))
                {
                    await SynchronizeAsync(sp, ct);
                }

                return;
            }

            await SynchronizeAsync(sp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow publish failed");
        }
    }

    private async Task SynchronizeAsync(IServiceProvider sp, CancellationToken ct)
    {
        var publisher = sp.GetRequiredService<IWorkflowDefinitionPublisher>();
        var store = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var reloader = sp.GetService<IWorkflowDefinitionsReloader>();
        var runtimeStorePopulator = sp.GetService<IWorkflowDefinitionStorePopulator>();

        var count = 0;
        foreach (var type in WorkflowTypes)
        {
            try
            {
                var defId = type.Name;
                var checksum = ComputeWorkflowChecksum(type);
                var filter = new WorkflowDefinitionFilter { DefinitionId = defId };
                var existing = await store.FindManyAsync(filter, ct);
                if (existing.Any(d => IsSynchronized(d, checksum)))
                {
                    _logger.LogDebug("Already synchronized: {Name}", defId);
                    continue;
                }

                // Build the workflow from C# code
                var workflow = (IWorkflow)ActivatorUtilities.CreateInstance(sp, type);
                var builder = sp.GetRequiredService<IWorkflowBuilderFactory>().CreateBuilder();
                var built = await builder.BuildWorkflowAsync(workflow, ct);
                AttachWorkflowVariables(builder);

                // Publish via Elsa's publisher (creates proper serialized definition)
                var def = await publisher.NewAsync(built.Root, ct);
                def.DefinitionId = defId;
                def.Name = defId;
                def.IsPublished = true;
                def.CustomProperties["Source"] = "CodeFirst";
                def.CustomProperties["SourceChecksum"] = checksum;
                def.CustomProperties["PublisherFormatVersion"] = PublisherFormatVersion;

                await publisher.SaveDraftAsync(def, ct);
                await publisher.PublishAsync(def, ct);
                count++;
                _logger.LogInformation("Synchronized: {Name}", defId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed: {Type}", type.Name);
            }
        }

        if (runtimeStorePopulator is not null)
        {
            await runtimeStorePopulator.PopulateStoreAsync(true, ct);
            _logger.LogInformation("Force-repopulated runtime workflow definition store");
        }

        if (reloader is not null)
        {
            await reloader.ReloadWorkflowDefinitionsAsync(ct);
            _logger.LogInformation("Reloaded runtime workflow definitions after synchronization");
        }

        _logger.LogInformation("Synchronized {Count} workflows ({Total} total types)", count, WorkflowTypes.Length);
    }

    private static bool IsSynchronized(Elsa.Workflows.Management.Entities.WorkflowDefinition definition, string checksum)
    {
        if (!definition.IsPublished)
            return false;

        if (!definition.CustomProperties.TryGetValue("SourceChecksum", out var checksumValue) ||
            !string.Equals(checksumValue?.ToString(), checksum, StringComparison.Ordinal))
            return false;

        return definition.CustomProperties.TryGetValue("PublisherFormatVersion", out var publisherVersion) &&
               string.Equals(publisherVersion?.ToString(), PublisherFormatVersion, StringComparison.Ordinal);
    }

    private static void AttachWorkflowVariables(IWorkflowBuilder builder)
    {
        if (builder.Root is not IVariableContainer variableContainer)
            return;

        variableContainer.Variables.Clear();

        foreach (var variable in builder.Variables)
            variableContainer.Variables.Add(variable);
    }

    private async Task PatchSchemaAsync(IServiceProvider sp, CancellationToken ct)
    {
        try
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var pgConn = configuration.GetConnectionString("MagicPai");
            if (string.IsNullOrWhiteSpace(pgConn))
                return;

            await using var conn = new Npgsql.NpgsqlConnection(pgConn);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'Elsa' AND table_name = 'WorkflowDefinitions') THEN
                        ALTER TABLE ""Elsa"".""WorkflowDefinitions"" ADD COLUMN IF NOT EXISTS ""OriginalSource"" TEXT NULL;
                    END IF;
                END $$;";
            await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogInformation("Schema patch applied (OriginalSource column)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema patch failed (will retry on next restart)");
        }
    }

    private static string ComputeWorkflowChecksum(Type workflowType)
    {
        // Use the compiled assembly module version ID instead of source paths so checksum
        // changes on every rebuild and still works when the app runs from published output.
        var assemblyVersionId = workflowType.Assembly.ManifestModule.ModuleVersionId.ToString("N");
        var identity = $"{assemblyVersionId}:{workflowType.FullName ?? workflowType.Name}";

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(identity);
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }
}
