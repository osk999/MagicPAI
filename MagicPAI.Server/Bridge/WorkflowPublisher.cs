using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Runtime;
using MagicPAI.Core.Config;
using Medallion.Threading;

namespace MagicPAI.Server.Bridge;

public class WorkflowPublisher : IHostedService
{
    private const string PublishLockName = "magicpai:workflow-publisher";
    private const string PublisherFormatVersion = "5";
    private const string WorkflowSchemaUrl = "https://elsaworkflows.io/schemas/3.x/workflow-definition.json";
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowPublisher> _logger;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private Task? _initializationTask;

    /// <summary>Resolve a friendly name to the DefinitionId (class name).</summary>
    public static string ResolveDefinitionId(string workflowName) => WorkflowCatalog.ResolveDefinitionId(workflowName);

    /// <summary>Resolve to DefinitionVersionId for dispatch.</summary>
    public static string ResolveDefinitionVersionId(string workflowName) => WorkflowCatalog.ResolveDefinitionVersionId(workflowName);

    public WorkflowPublisher(IServiceProvider services, ILogger<WorkflowPublisher> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct) => EnsureInitializedAsync(ct);

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task InitializeAsync(CancellationToken ct) => EnsureInitializedAsync(ct);

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initializationTask is null)
        {
            await _initializeLock.WaitAsync(ct);
            try
            {
                _initializationTask ??= InitializeCoreAsync(ct);
            }
            finally
            {
                _initializeLock.Release();
            }
        }

        await _initializationTask.WaitAsync(ct);
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            await PatchSchemaAsync(sp, ct);

            var config = sp.GetRequiredService<MagicPaiConfig>();

            if (string.Equals(config.ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var connectionString = configuration.GetConnectionString("MagicPai");
                var lockProvider = PostgresDistributedLockFactory.Create(connectionString!);

                _logger.LogInformation("Acquiring distributed workflow publish lock");
                await using (await lockProvider.AcquireLockAsync(PublishLockName, null, ct))
                    await SynchronizeAsync(sp, ct);

                return;
            }

            await SynchronizeAsync(sp, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow publish failed");
            throw;
        }
    }

    private async Task SynchronizeAsync(IServiceProvider sp, CancellationToken ct)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var environment = sp.GetRequiredService<IHostEnvironment>();
        var publisher = sp.GetRequiredService<IWorkflowDefinitionPublisher>();
        var importer = sp.GetRequiredService<IWorkflowDefinitionImporter>();
        var store = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var reloader = sp.GetService<IWorkflowDefinitionsReloader>();
        var runtimeStorePopulator = sp.GetService<IWorkflowDefinitionStorePopulator>();
        var templateDirectory = Path.Combine(environment.ContentRootPath, "Workflows", "Templates");
        var refreshTemplatesFromCode = configuration.GetValue<bool>("MagicPAI:WorkflowTemplates:RefreshFromCode");

        if (refreshTemplatesFromCode)
        {
            await ExportTemplatesAsync(sp, templateDirectory, ct);
            _logger.LogInformation("Regenerated workflow JSON templates from current workflow classes");
        }

        var count = 0;

        foreach (var entry in WorkflowCatalog.Entries)
        {
            try
            {
                var templatePath = Path.Combine(templateDirectory, entry.TemplateFileName);
                var hasTemplate = entry.UseJsonTemplate && File.Exists(templatePath);
                var checksum = hasTemplate
                    ? await ComputeTemplateChecksumAsync(templatePath, ct)
                    : ComputeWorkflowChecksum(entry.WorkflowType);
                var existing = await store.FindManyAsync(new WorkflowDefinitionFilter
                {
                    DefinitionId = entry.DefinitionId
                }, ct);

                if (existing.Any(d => IsSynchronized(d, checksum)))
                {
                    _logger.LogDebug("Already synchronized: {Name}", entry.DefinitionId);
                    continue;
                }

                if (hasTemplate)
                    await ImportTemplateAsync(sp, importer, entry, templatePath, checksum, ct);
                else
                    await PublishCodeFirstAsync(sp, publisher, entry, checksum, ct);

                count++;
                _logger.LogInformation("Synchronized: {Name}", entry.DefinitionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed: {Type}", entry.DefinitionId);
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

        _logger.LogInformation("Synchronized {Count} workflows ({Total} total templates)", count, WorkflowCatalog.Entries.Count);
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

    private async Task ImportTemplateAsync(
        IServiceProvider sp,
        IWorkflowDefinitionImporter importer,
        WorkflowCatalogEntry entry,
        string templatePath,
        string checksum,
        CancellationToken ct)
    {
        var serializer = sp.GetRequiredService<IActivitySerializer>();
        var json = await File.ReadAllTextAsync(templatePath, ct);
        var model = serializer.Deserialize<WorkflowDefinitionModel>(json);
        model.DefinitionId = entry.DefinitionId;
        model.Name = string.IsNullOrWhiteSpace(model.Name) ? entry.DefinitionId : model.Name.Trim();
        model.IsPublished = true;
        model.CustomProperties ??= new Dictionary<string, object>();
        model.CustomProperties["Source"] = "JsonTemplate";
        model.CustomProperties["SourceChecksum"] = checksum;
        model.CustomProperties["PublisherFormatVersion"] = PublisherFormatVersion;
        model.CustomProperties["TemplateFileName"] = entry.TemplateFileName;

        var result = await importer.ImportAsync(new SaveWorkflowDefinitionRequest
        {
            Model = model,
            Publish = true
        }, ct);

        if (!result.Succeeded)
        {
            var message = string.Join("; ", result.ValidationErrors.Select(x => x.Message));
            throw new InvalidOperationException(
                $"Failed to import workflow template '{entry.TemplateFileName}': {message}");
        }
    }

    private async Task PublishCodeFirstAsync(
        IServiceProvider sp,
        IWorkflowDefinitionPublisher publisher,
        WorkflowCatalogEntry entry,
        string checksum,
        CancellationToken ct)
    {
        var built = await BuildWorkflowAsync(sp, entry.WorkflowType, ct);
        var definition = await publisher.NewAsync(built, ct);
        definition.DefinitionId = entry.DefinitionId;
        definition.Name = built.WorkflowMetadata.Name ?? entry.DefinitionId;
        definition.Description = built.WorkflowMetadata.Description;
        definition.Inputs = built.Inputs.ToList();
        definition.Outputs = built.Outputs.ToList();
        definition.Outcomes = built.Outcomes.ToList();
        definition.IsPublished = true;
        definition.CustomProperties["Source"] = "CodeFirst";
        definition.CustomProperties["SourceChecksum"] = checksum;
        definition.CustomProperties["PublisherFormatVersion"] = PublisherFormatVersion;
        definition.CustomProperties["TemplateFileName"] = entry.TemplateFileName;

        await publisher.SaveDraftAsync(definition, ct);
        await publisher.PublishAsync(definition, ct);
    }

    private async Task ExportTemplatesAsync(IServiceProvider sp, string templateDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(templateDirectory);
        var serializer = sp.GetRequiredService<IWorkflowSerializer>();

        foreach (var entry in WorkflowCatalog.Entries.Where(x => x.UseJsonTemplate))
        {
            var workflow = await BuildWorkflowAsync(sp, entry.WorkflowType, ct);
            var serialized = serializer.Serialize(workflow);
            var normalized = NormalizeTemplateJson(serialized);
            var templatePath = Path.Combine(templateDirectory, entry.TemplateFileName);
            await File.WriteAllTextAsync(templatePath, normalized, Encoding.UTF8, ct);
        }
    }

    private static async Task<Workflow> BuildWorkflowAsync(IServiceProvider sp, Type workflowType, CancellationToken ct)
    {
        var workflow = (IWorkflow)ActivatorUtilities.CreateInstance(sp, workflowType);
        var builder = sp.GetRequiredService<IWorkflowBuilderFactory>().CreateBuilder();
        var builtWorkflow = await builder.BuildWorkflowAsync(workflow, ct);
        await ApplyAsyncActivityFlagsAsync(sp, builtWorkflow, ct);
        return builtWorkflow;
    }

    private static async Task ApplyAsyncActivityFlagsAsync(IServiceProvider sp, Workflow workflow, CancellationToken ct)
    {
        var graphBuilder = sp.GetRequiredService<IWorkflowGraphBuilder>();
        var graph = await graphBuilder.BuildAsync(workflow, ct);

        foreach (var node in graph.Nodes)
        {
            if (node.Activity is not Activity activity)
                continue;

            var attribute = activity.GetType().GetCustomAttribute<ActivityAttribute>();
            if (attribute?.RunAsynchronously == true && activity.RunAsynchronously != true)
                activity.RunAsynchronously = true;
        }
    }

    private static string NormalizeTemplateJson(string serialized)
    {
        var parsed = JsonNode.Parse(serialized)?.AsObject()
            ?? throw new InvalidOperationException("Workflow serializer returned invalid JSON.");

        if (!parsed.ContainsKey("$schema"))
        {
            var withSchema = new JsonObject
            {
                ["$schema"] = WorkflowSchemaUrl
            };

            foreach (var property in parsed)
                withSchema[property.Key] = property.Value?.DeepClone();

            parsed = withSchema;
        }

        return parsed.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void AttachWorkflowVariables(IActivity root, IEnumerable<Variable> variables)
    {
        if (root is not IVariableContainer variableContainer)
            return;

        foreach (var variable in variables)
        {
            var exists = variableContainer.Variables.Any(existing =>
                string.Equals(existing.Id, variable.Id, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(existing.Name) &&
                 string.Equals(existing.Name, variable.Name, StringComparison.Ordinal)));

            if (!exists)
                variableContainer.Variables.Add(variable);
        }
    }

    private async Task PatchSchemaAsync(IServiceProvider sp, CancellationToken ct)
    {
        try
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var pgConn = configuration.GetConnectionString("MagicPai");
            if (string.IsNullOrWhiteSpace(pgConn))
                return;

            if (pgConn.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                await using var sqliteConn = new Microsoft.Data.Sqlite.SqliteConnection(pgConn);
                await sqliteConn.OpenAsync(ct);
                try
                {
                    await using var checkCmd = sqliteConn.CreateCommand();
                    checkCmd.CommandText = "SELECT OriginalSource FROM WorkflowDefinitions LIMIT 0";
                    await checkCmd.ExecuteNonQueryAsync(ct);
                }
                catch
                {
                    try
                    {
                        await using var alterCmd = sqliteConn.CreateCommand();
                        alterCmd.CommandText = "ALTER TABLE WorkflowDefinitions ADD COLUMN OriginalSource TEXT";
                        await alterCmd.ExecuteNonQueryAsync(ct);
                        _logger.LogInformation("SQLite schema patch applied (OriginalSource column)");
                    }
                    catch (Exception ex2)
                    {
                        _logger.LogWarning(ex2, "SQLite schema patch failed");
                    }
                }
                return;
            }

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
        var assemblyVersionId = workflowType.Assembly.ManifestModule.ModuleVersionId.ToString("N");
        var identity = $"{assemblyVersionId}:{workflowType.FullName ?? workflowType.Name}";

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(identity);
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }

    private static async Task<string> ComputeTemplateChecksumAsync(string templatePath, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(templatePath, ct);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(content)));
    }
}
