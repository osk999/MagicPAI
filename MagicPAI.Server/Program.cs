using FastEndpoints;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using MagicPAI.Activities.AI;
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Gates;
using Elsa.Workflows;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Providers;
using MagicPAI.Server.Services;
using MagicPAI.Server.Workflows;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// NOTE: Do NOT use UseStaticWebAssets() — it rewrites index.html and removes
// script tags that don't match the fingerprint map. UseBlazorFrameworkFiles()
// + UseStaticFiles() handles WASM hosting without rewriting.

// Disable Elsa API security in Development (per Elsa docs)
if (builder.Environment.IsDevelopment())
{
    // Try both old and new type locations
    var secType = Type.GetType("Elsa.Api.Common.Options.EndpointSecurityOptions, Elsa.Api.Common")
        ?? AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => t.Name == "EndpointSecurityOptions");
    secType?.GetMethod("DisableSecurity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
    if (secType is not null)
        Console.WriteLine($"Elsa security disabled via {secType.FullName}");
    else
        Console.WriteLine("WARNING: Could not find EndpointSecurityOptions to disable Elsa API security");
}

// --- Configuration ---
var config = builder.Configuration.GetSection("MagicPAI").Get<MagicPaiConfig>() ?? new MagicPaiConfig();
var configErrors = config.Validate();
if (configErrors.Count > 0)
    throw new InvalidOperationException(
        "Invalid MagicPAI configuration:" + Environment.NewLine + string.Join(Environment.NewLine, configErrors.Select(x => $"- {x}")));
builder.Services.AddSingleton(config);

var pgConn = builder.Configuration.GetConnectionString("MagicPai");
var useKubernetesBackend = string.Equals(config.ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase);

if (string.IsNullOrWhiteSpace(pgConn))
    throw new InvalidOperationException(
        "ConnectionStrings:MagicPai is required. MagicPAI uses PostgreSQL for Elsa workflow persistence.");

// --- Core Services ---
builder.Services.AddSingleton<SharedBlackboard>();
if (useKubernetesBackend)
    builder.Services.AddSingleton<IContainerManager, KubernetesContainerManager>();
else if (config.UseDocker)
    builder.Services.AddSingleton<IContainerManager, DockerContainerManager>();
else
    builder.Services.AddSingleton<IContainerManager, LocalContainerManager>();
builder.Services.AddSingleton<ICliAgentFactory, CliAgentFactory>();
builder.Services.AddSingleton<IExecutionEnvironment, LocalExecutionEnvironment>();
builder.Services.AddSingleton<WorktreeManager>();

// --- Verification Gates ---
builder.Services.AddSingleton<IVerificationGate, CompileGate>();
builder.Services.AddSingleton<IVerificationGate, TestGate>();
builder.Services.AddSingleton<IVerificationGate, CoverageGate>();
builder.Services.AddSingleton<IVerificationGate, SecurityGate>();
builder.Services.AddSingleton<IVerificationGate, LintGate>();
builder.Services.AddSingleton<IVerificationGate, HallucinationDetector>();
builder.Services.AddSingleton<IVerificationGate, QualityReviewGate>();
builder.Services.AddSingleton<VerificationPipeline>();

// --- SignalR ---
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

// --- Event Bridge Services ---
builder.Services.AddSingleton<SessionTracker>();
builder.Services.AddSingleton<SessionHistoryReader>();

builder.Services.AddElsa(elsa =>
{
    // Workflow Management — PostgreSQL
    elsa.UseWorkflowManagement(management =>
    {
        management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(pgConn));
    });

    // Workflow Runtime — PostgreSQL
    elsa.UseWorkflowRuntime(runtime =>
    {
        runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(pgConn));

        if (useKubernetesBackend)
        {
            runtime.DistributedLockProvider = _ => PostgresDistributedLockFactory.Create(pgConn!);
            runtime.DistributedLockingOptions = options =>
            {
                options.LockAcquisitionTimeout = TimeSpan.FromSeconds(30);
            };
        }

        // Register built-in workflows
        runtime.AddWorkflow<FullOrchestrateWorkflow>();
        runtime.AddWorkflow<SimpleAgentWorkflow>();
        runtime.AddWorkflow<VerifyAndRepairWorkflow>();
        runtime.AddWorkflow<PromptEnhancerWorkflow>();
        runtime.AddWorkflow<ContextGathererWorkflow>();
        runtime.AddWorkflow<PromptGroundingWorkflow>();
        runtime.AddWorkflow<LoopVerifierWorkflow>();
        runtime.AddWorkflow<WebsiteAuditLoopWorkflow>();
        runtime.AddWorkflow<IsComplexAppWorkflow>();
        runtime.AddWorkflow<IsWebsiteProjectWorkflow>();
        runtime.AddWorkflow<OrchestrateComplexPathWorkflow>();
        runtime.AddWorkflow<OrchestrateSimplePathWorkflow>();
        runtime.AddWorkflow<PostExecutionPipelineWorkflow>();
        runtime.AddWorkflow<ResearchPipelineWorkflow>();
        runtime.AddWorkflow<StandardOrchestrateWorkflow>();
        runtime.AddWorkflow<TestSetPromptWorkflow>();
        runtime.AddWorkflow<ClawEvalAgentWorkflow>();
    });

    // Identity
    elsa.UseIdentity(identity =>
    {
        identity.TokenOptions = tokenOptions =>
        {
            tokenOptions.SigningKey = builder.Configuration["Elsa:Identity:SigningKey"]
                ?? "sufficiently-long-secret-key-for-development-only-replace-in-production";
            tokenOptions.AccessTokenLifetime = TimeSpan.FromDays(1);
        };
        identity.UseAdminUserProvider();
    });

    // Default Authentication
    elsa.UseDefaultAuthentication(auth => auth.UseAdminApiKey());

    // HTTP activities
    elsa.UseHttp(http => http.ConfigureHttpOptions = options =>
    {
        var baseUrl = builder.Configuration["Elsa:Http:BaseUrl"] ?? "https://localhost:5001";
        options.BaseUrl = new Uri(baseUrl);
    });

    // Scheduling (timers, delays, cron)
    elsa.UseScheduling();

    // Expression languages
    elsa.UseJavaScript(options => options.AllowClrAccess = true);
    elsa.UseCSharp();
    elsa.UseLiquid((Elsa.Liquid.Features.LiquidFeature liquid) => { });

    // Workflows API (handles FastEndpoints + serialization internally)
    elsa.UseWorkflowsApi();

    // Register all custom activities from MagicPAI.Activities assembly
    elsa.AddActivitiesFrom<RunCliAgentActivity>();
});

// --- Notification handlers ---
builder.Services.AddNotificationHandler<ElsaEventBridge>();
builder.Services.AddNotificationHandler<WorkflowProgressTracker>();
builder.Services.AddNotificationHandler<WorkflowCompletionHandler>();

// --- Activity descriptor customization (icons/colors in Studio) ---
builder.Services.AddSingleton<IActivityDescriptorModifier, MagicPaiActivityDescriptorModifier>();

// --- FastEndpoints (required for Elsa API in 3.6) ---
builder.Services.AddFastEndpoints();

// --- Workflow publisher (materializes code-first workflows for Studio) ---
builder.Services.AddHostedService<WorkflowPublisher>();

// --- Worker pod/container garbage collector ---
builder.Services.AddHostedService<WorkerPodGarbageCollector>();

// --- Controllers + Razor Pages (for _Host.cshtml WASM fallback) ---
builder.Services.AddControllers();
builder.Services.AddRazorPages();

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("*");
    });
});

var app = builder.Build();

// --- Middleware (order follows official Elsa reference app) ---
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

app.UseCors();

// Blazor WASM framework files (from MagicPAI.Studio project)
app.UseBlazorFrameworkFiles();

// WASM asset path rewriting (must be BEFORE UseStaticFiles)
app.UseMiddleware<MagicPAI.Server.Middleware.WasmAssetsRewritingMiddleware>();

// Static files — use traditional UseStaticFiles (UseStaticWebAssets rewrites script tags)
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".blat"] = "application/octet-stream";
provider.Mappings[".pdb"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Elsa API via FastEndpoints
app.UseFastEndpoints(cfg =>
{
    cfg.Serializer.Options.Converters.Insert(0, new MagicPAI.Server.Bridge.TypeJsonConverter());
});

// JSON error handler
app.UseJsonSerializationErrorHandler();

// Elsa HTTP workflow endpoint activities
app.UseWorkflows();

// Custom endpoints
app.MapHealthChecks("/health", new HealthCheckOptions());
app.MapHealthChecks("/health/live", new HealthCheckOptions());
app.MapHealthChecks("/health/ready", new HealthCheckOptions());
app.MapControllers();
app.MapRazorPages();
app.MapHub<SessionHub>("/hub");
app.MapFallbackToPage("/_Host");

app.Run();

// Expose Program class for WebApplicationFactory<Program> in integration tests
public partial class Program { }
