// MagicPAI.Server/Program.cs
// Temporal-only host (Phase 3 Day 13: Elsa retired). Registers:
//   • MagicPAI core services (container manager, verification gates, config).
//   • SignalR hub for session telemetry.
//   • Temporal client + hosted worker with all activity groups and workflows.
//   • Blazor WASM middleware for the MagicPAI Studio frontend.
// See temporal.md §M.1 for the canonical shape.
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Gates;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Git;
using MagicPAI.Activities.Infrastructure;
using MagicPAI.Activities.Verification;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Services;
using MagicPAI.Server.Workflows;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Temporalio.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// UseStaticWebAssets() discovers _framework/ files from the MagicPAI.Studio WASM project.
// Required for both Development and Production when using `dotnet run`.
builder.WebHost.UseStaticWebAssets();

// --- Configuration ---
var config = builder.Configuration.GetSection("MagicPAI").Get<MagicPaiConfig>() ?? new MagicPaiConfig();
var configErrors = config.Validate();
if (configErrors.Count > 0)
    throw new InvalidOperationException(
        "Invalid MagicPAI configuration:" + Environment.NewLine + string.Join(Environment.NewLine, configErrors.Select(x => $"- {x}")));
builder.Services.AddSingleton(config);

var pgConn = builder.Configuration.GetConnectionString("MagicPai");
var useKubernetesBackend = string.Equals(config.ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase);

// --- Core Services ---
builder.Services.AddSingleton<SharedBlackboard>();
builder.Services.AddSingleton(new MagicPAI.Core.Services.Auth.AuthRecoveryService(config));
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

// --- Session stream sink (SignalR-backed side channel for Temporal activity output) ---
builder.Services.AddSingleton<ISessionStreamSink, SignalRSessionStreamSink>();

// --- Temporal client + hosted worker ---
{
    var temporalHost = builder.Configuration["Temporal:Host"] ?? "localhost:7233";
    var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "magicpai";
    var temporalTaskQueue = builder.Configuration["Temporal:TaskQueue"] ?? "magicpai-main";

    // Singleton ITemporalClient for use by controllers, hubs, hosted services.
    builder.Services.AddTemporalClient(opts =>
    {
        opts.TargetHost = temporalHost;
        opts.Namespace = temporalNamespace;
    });

    // Hosted worker — hosts activity executions against the task queue.
    builder.Services
        .AddHostedTemporalWorker(
            clientTargetHost: temporalHost,
            clientNamespace: temporalNamespace,
            taskQueue: temporalTaskQueue)
        .AddScopedActivities<DockerActivities>()
        .AddScopedActivities<AiActivities>()
        .AddScopedActivities<GitActivities>()
        .AddScopedActivities<VerifyActivities>()
        .AddScopedActivities<BlackboardActivities>()
        .AddWorkflow<SimpleAgentWorkflow>()
        .AddWorkflow<VerifyAndRepairWorkflow>()
        .AddWorkflow<PromptEnhancerWorkflow>()
        .AddWorkflow<ContextGathererWorkflow>()
        .AddWorkflow<PromptGroundingWorkflow>()
        .AddWorkflow<OrchestrateSimplePathWorkflow>()
        .AddWorkflow<ComplexTaskWorkerWorkflow>()
        .AddWorkflow<OrchestrateComplexPathWorkflow>()
        .AddWorkflow<PostExecutionPipelineWorkflow>()
        .AddWorkflow<ResearchPipelineWorkflow>()
        .AddWorkflow<StandardOrchestrateWorkflow>()
        .AddWorkflow<ClawEvalAgentWorkflow>()
        .AddWorkflow<WebsiteAuditCoreWorkflow>()
        .AddWorkflow<WebsiteAuditLoopWorkflow>()
        .AddWorkflow<FullOrchestrateWorkflow>()
        .AddWorkflow<DeepResearchOrchestrateWorkflow>();
}

// --- Session / workflow catalog services ---
builder.Services.AddSingleton<SessionTracker>();
builder.Services.AddSingleton<ISessionContainerRegistry>(sp => sp.GetRequiredService<SessionTracker>());
builder.Services.AddSingleton<WorkflowCatalog>();
builder.Services.AddSingleton<SessionHistoryReader>();
builder.Services.AddSingleton<IGuiPortAllocator, GuiPortAllocator>();
builder.Services.AddSingleton<SessionContainerLogStreamer>();
builder.Services.AddSingleton<ISessionContainerLogStreamer>(sp => sp.GetRequiredService<SessionContainerLogStreamer>());
builder.Services.AddSingleton<SessionLaunchPlanner>();

// --- Temporal-side infrastructure ---
builder.Services.AddSingleton<MagicPaiMetrics>();
builder.Services.AddSingleton<IStartupValidator, DockerEnforcementValidator>();
builder.Services.AddHostedService<SearchAttributesInitializer>();
builder.Services.AddHostedService<WorkflowCompletionMonitor>();

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

// --- Startup validation ---
// Runs before the Temporal worker processes anything so a misconfigured backend
// fails fast instead of silently falling back to local mode.
app.Services.GetRequiredService<IStartupValidator>().Validate();

// --- Middleware ---
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
