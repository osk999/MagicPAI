using FastEndpoints;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Microsoft.EntityFrameworkCore;
using Elsa.Extensions;
using MagicPAI.Activities.AI;
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;
using MagicPAI.Core.Services.Gates;
using Elsa.Workflows;
using Elsa.Tenants.Extensions;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Hubs;
using MagicPAI.Server.Providers;
using MagicPAI.Server.Services;
using MagicPAI.Server.Workflows;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// UseStaticWebAssets() discovers _framework/ files from the MagicPAI.Studio WASM project.
// Required for both Development and Production when using `dotnet run`.
builder.WebHost.UseStaticWebAssets();

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
var useSqlite = !string.IsNullOrWhiteSpace(pgConn) && pgConn.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);

if (string.IsNullOrWhiteSpace(pgConn))
    throw new InvalidOperationException(
        "ConnectionStrings:MagicPai is required. Use PostgreSQL or SQLite (Data Source=magicpai.db).");

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

// --- Event Bridge Services ---
builder.Services.AddSingleton<SessionTracker>();
builder.Services.AddSingleton<ISessionContainerRegistry>(sp => sp.GetRequiredService<SessionTracker>());
builder.Services.AddSingleton<SessionHistoryReader>();
builder.Services.AddSingleton<IGuiPortAllocator, GuiPortAllocator>();
builder.Services.AddSingleton<SessionContainerLogStreamer>();
builder.Services.AddSingleton<ISessionContainerLogStreamer>(sp => sp.GetRequiredService<SessionContainerLogStreamer>());
builder.Services.AddSingleton<SessionLaunchPlanner>();

builder.Services.AddElsa(elsa =>
{
    // Workflow Management — PostgreSQL or SQLite
    elsa.UseWorkflowManagement(management =>
    {
        management.UseEntityFrameworkCore(ef =>
        {
            if (useSqlite) { ef.UseSqlite(pgConn); ef.RunMigrations = false; }
            else ef.UsePostgreSql(pgConn);
        });
    });

    // Workflow Runtime — PostgreSQL or SQLite
    elsa.UseWorkflowRuntime(runtime =>
    {
        runtime.UseEntityFrameworkCore(ef =>
        {
            if (useSqlite) { ef.UseSqlite(pgConn); ef.RunMigrations = false; }
            else ef.UsePostgreSql(pgConn);
        });

        if (useKubernetesBackend)
        {
            runtime.DistributedLockProvider = _ => PostgresDistributedLockFactory.Create(pgConn!);
            runtime.DistributedLockingOptions = options =>
            {
                options.LockAcquisitionTimeout = TimeSpan.FromSeconds(30);
            };
        }

        // These workflows still rely on delegate-built child input/prompt expressions
        // that are not preserved correctly by the current JSON template exporter.
        runtime.AddWorkflow<FullOrchestrateWorkflow>();
        runtime.AddWorkflow<WebsiteAuditLoopWorkflow>();
        runtime.AddWorkflow<ComplexTaskWorkerWorkflow>();
        runtime.AddWorkflow<DeepResearchOrchestrateWorkflow>();

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

    // HTTP activities.
    // BasePath moved off "/workflows" (Elsa's default) so it doesn't 404-intercept
    // Studio's client-side routes at /workflows/definitions and /workflows/instances.
    elsa.UseHttp(http => http.ConfigureHttpOptions = options =>
    {
        var baseUrl = builder.Configuration["Elsa:Http:BaseUrl"] ?? "https://localhost:5001";
        options.BaseUrl = new Uri(baseUrl);
        options.BasePath = "/webhooks";
    });

    // Scheduling (timers, delays, cron)
    elsa.UseScheduling();

    // Expression languages
    elsa.UseJavaScript(options => options.AllowClrAccess = true);
    elsa.UseCSharp();
    elsa.UseLiquid((Elsa.Liquid.Features.LiquidFeature liquid) => { });

    // Workflows API (handles FastEndpoints + serialization internally)
    elsa.UseWorkflowsApi();
    elsa.UseRealTimeWorkflows();

    // Multi-tenancy + Labels (required by Elsa 3.5+ even for single-tenant)
    elsa.UseTenants(tenants => tenants.UseTenantManagement());
    elsa.UseLabels(labels => { });

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

// --- Workflow publisher (imports JSON workflow templates into Elsa stores) ---
builder.Services.AddSingleton<WorkflowPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkflowPublisher>());

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

// SQLite schema fixup: EnsureCreated doesn't work with multiple DbContexts sharing one DB.
// We use raw SQL to create any tables the Elsa 3.5.x migrations missed.
if (useSqlite)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SQLiteSchemaSetup");
    using var scope = app.Services.CreateScope();

    // Get each DbContext, generate its SQL model, and apply missing tables
    foreach (var contextType in new[] {
        typeof(Elsa.EntityFrameworkCore.Modules.Management.ManagementElsaDbContext),
        typeof(Elsa.EntityFrameworkCore.Modules.Runtime.RuntimeElsaDbContext) })
    {
        try
        {
            var factoryType = typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<>).MakeGenericType(contextType);
            var factory = scope.ServiceProvider.GetService(factoryType);
            if (factory is null) continue;
            var createMethod = factoryType.GetMethod("CreateDbContext");
            if (createMethod is null) continue;
            using var db = (Microsoft.EntityFrameworkCore.DbContext)createMethod.Invoke(factory, null)!;
            var script = db.Database.GenerateCreateScript();
            // Execute each CREATE TABLE IF NOT EXISTS statement
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(pgConn);
            conn.Open();
            foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(statement)) continue;
                var sql = statement.Replace("CREATE TABLE", "CREATE TABLE IF NOT EXISTS", StringComparison.OrdinalIgnoreCase);
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SQLite schema statement skipped: {Sql}", sql[..Math.Min(sql.Length, 80)]);
                }
            }
            logger.LogInformation("Schema setup completed for {ContextType}", contextType.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema setup failed for {ContextType}", contextType.Name);
        }
    }
}

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

// Elsa API via FastEndpoints. Use Elsa's middleware so /elsa/api/* is actually mapped
// instead of falling through to the Blazor fallback shell.
app.UseWorkflowsApi("elsa/api");

// JSON error handler
app.UseJsonSerializationErrorHandler();

// Elsa HTTP workflow endpoint activities
app.UseWorkflows();
app.UseWorkflowsSignalRHubs();

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
