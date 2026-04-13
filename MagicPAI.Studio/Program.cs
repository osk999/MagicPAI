using Elsa.Studio.Core.BlazorWasm.Extensions;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Login.BlazorWasm.Extensions;
using Elsa.Studio.Login.Extensions;
using Elsa.Studio.Options;
using Elsa.Studio.Shell;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Contracts;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Elsa.Studio.Contracts;
using MagicPAI.Studio;
using MagicPAI.Studio.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Elsa.Studio.Shell.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.RootComponents.RegisterCustomElsaStudioElements();

var backendUri = BackendUrlResolver.ResolveBackendUri(
    builder.Configuration,
    builder.HostEnvironment);
var elsaApiUri = new Uri(backendUri, "elsa/api");
var studioConnection = new ElsaStudioConnectionSettings();

// --- MagicPAI services ---
builder.Services.AddScoped<SessionHubClient>();
builder.Services.AddScoped<WorkflowInstanceLiveUpdater>();
builder.Services.AddSingleton(studioConnection);
builder.Services.AddScoped<SessionApiClient>(_ =>
{
    var httpClient = new HttpClient { BaseAddress = backendUri };
    return new SessionApiClient(httpClient);
});

// --- Elsa Studio services ---
builder.Services.AddCore();
builder.Services.AddShell(options => options.DisableAuthorization = true);

// Login module registers IAuthenticationProviderManager needed by WorkflowInstanceObserverFactory
builder.Services.AddLoginModule().UseElsaIdentity();

// Remote backend with API key auth (overrides login module's auth handler)
builder.Services.AddTransient<ElsaStudioApiKeyHandler>();
builder.Services.AddRemoteBackend(new()
{
    ConfigureBackendOptions = options => options.Url = elsaApiUri,
    ConfigureHttpClientBuilder = options =>
        options.AuthenticationHandler = typeof(ElsaStudioApiKeyHandler)
});

builder.Services.AddDashboardModule();
builder.Services.AddWorkflowsModule();
builder.Services.AddWorkflowsDesigner();
builder.Services.AddScoped<IWorkflowInstanceObserverFactory, MagicPaiWorkflowInstanceObserverFactory>();

// Custom MagicPAI menu provider + feature (for page discovery by Shell.App)
builder.Services.AddScoped<IMenuProvider, MagicPaiMenuProvider>();
builder.Services.AddScoped<IMenuGroupProvider, MagicPaiMenuGroupProvider>();
builder.Services.AddScoped<IFeature, MagicPaiFeature>();

var app = builder.Build();

// Set backend URL (in case getClientConfig is available from _Host.cshtml)
try
{
    var js = app.Services.GetRequiredService<IJSRuntime>();
    var clientConfig = await js.InvokeAsync<JsonElement>("getClientConfig");
    var apiUrl = clientConfig.GetProperty("apiUrl").GetString();
    var elsaApiUrl = clientConfig.TryGetProperty("elsaApiUrl", out var elsaApiProp)
        ? elsaApiProp.GetString()
        : null;
    if (!string.IsNullOrEmpty(elsaApiUrl))
        app.Services.GetRequiredService<IOptions<BackendOptions>>().Value.Url = new Uri(elsaApiUrl);
    else if (!string.IsNullOrEmpty(apiUrl))
        app.Services.GetRequiredService<IOptions<BackendOptions>>().Value.Url = new Uri(new Uri(apiUrl), "elsa/api");
    if (clientConfig.TryGetProperty("elsaApiKey", out var apiKeyProp))
    {
        var apiKey = apiKeyProp.GetString();
        if (!string.IsNullOrWhiteSpace(apiKey))
            app.Services.GetRequiredService<ElsaStudioConnectionSettings>().ApiKey = apiKey;
    }
}
catch
{
    // getClientConfig not available (standalone WASM) — use backendUri already set
}

// Run Elsa Studio startup tasks
var startupTaskRunner = app.Services.GetRequiredService<IStartupTaskRunner>();
await startupTaskRunner.RunStartupTasksAsync();

await app.RunAsync();
