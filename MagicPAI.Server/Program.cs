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

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var config = builder.Configuration.GetSection("MagicPAI").Get<MagicPaiConfig>() ?? new MagicPaiConfig();
builder.Services.AddSingleton(config);

// --- Core Services ---
builder.Services.AddSingleton<SharedBlackboard>();
if (!config.UseDocker)
    builder.Services.AddSingleton<IContainerManager, LocalContainerManager>();
else if (config.ExecutionBackend == "kubernetes")
    builder.Services.AddSingleton<IContainerManager, KubernetesContainerManager>();
else
    builder.Services.AddSingleton<IContainerManager, DockerContainerManager>();
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

// --- Event Bridge Services ---
builder.Services.AddSingleton<SessionTracker>();

// --- Elsa Workflows (following official reference app pattern) ---
var pgConn = builder.Configuration.GetConnectionString("MagicPai");

builder.Services.AddElsa(elsa =>
{
    // Workflow Management — PostgreSQL if connection string provided, else SQLite
    elsa.UseWorkflowManagement(management =>
    {
        if (!string.IsNullOrEmpty(pgConn))
            management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(pgConn));
        else
            management.UseEntityFrameworkCore(ef => ef.UseSqlite());
    });

    // Workflow Runtime — same conditional persistence
    elsa.UseWorkflowRuntime(runtime =>
    {
        if (!string.IsNullOrEmpty(pgConn))
            runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(pgConn));
        else
            runtime.UseEntityFrameworkCore(ef => ef.UseSqlite());

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

    // HTTP activities (with configurable base URL for bookmark resume URLs)
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

// --- Controllers ---
builder.Services.AddControllers();

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

// Static files with Blazor WASM types
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".blat"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Elsa API via FastEndpoints
app.UseFastEndpoints(cfg =>
{
    // Handle System.Type/RuntimeType serialization for Elsa descriptors
    cfg.Serializer.Options.Converters.Insert(0, new MagicPAI.Server.Bridge.TypeJsonConverter());
});

// JSON error handler
app.UseJsonSerializationErrorHandler();

// Elsa HTTP workflow endpoint activities
app.UseWorkflows();

// Custom endpoints
app.MapControllers();
app.MapHub<SessionHub>("/hub");

app.Run();
