using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using MagicPAI.Activities.AI;
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Gates;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Workflows;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var config = builder.Configuration.GetSection("MagicPAI").Get<MagicPaiConfig>() ?? new MagicPaiConfig();
builder.Services.AddSingleton(config);

// --- Core Services ---
builder.Services.AddSingleton<SharedBlackboard>();
builder.Services.AddSingleton<IContainerManager, DockerContainerManager>();
builder.Services.AddSingleton<ICliAgentFactory, CliAgentFactory>();

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

// --- Event Bridge Services ---
builder.Services.AddSingleton<SessionTracker>();

// --- Elsa Workflows ---
builder.Services.AddElsa(elsa =>
{
    // Workflow Management with SQLite persistence
    elsa.UseWorkflowManagement(management =>
    {
        management.UseEntityFrameworkCore(ef =>
            ef.UseSqlite());
    });

    // Workflow Runtime with SQLite persistence
    elsa.UseWorkflowRuntime(runtime =>
    {
        runtime.UseEntityFrameworkCore(ef =>
            ef.UseSqlite());

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
    elsa.UseDefaultAuthentication(auth =>
    {
        auth.UseAdminApiKey();
    });

    // HTTP activities (for webhook triggers, etc.)
    elsa.UseHttp();

    // Workflows API endpoints (FastEndpoints-based)
    elsa.UseWorkflowsApi();

    // Register all custom activities from MagicPAI.Activities assembly
    elsa.AddActivitiesFrom<RunCliAgentActivity>();
});

// --- Notification handlers for Elsa event bridge ---
builder.Services.AddNotificationHandler<ElsaEventBridge>();
builder.Services.AddNotificationHandler<WorkflowProgressTracker>();

// --- Controllers ---
builder.Services.AddControllers();

// --- CORS (for Blazor WASM client) ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// --- Middleware ---
app.UseCors();

// Static files (Blazor WASM will be served if published alongside)
app.UseStaticFiles();

// Routing
app.UseRouting();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Elsa HTTP workflow middleware
app.UseWorkflows();

// Map endpoints
app.MapControllers();
app.MapHub<SessionHub>("/hub");
app.MapFallbackToFile("index.html");

app.Run();
