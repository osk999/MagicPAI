using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Elsa.Studio.Core.BlazorWasm.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Login.BlazorWasm.Extensions;
using Elsa.Studio.Login.Extensions;
using Elsa.Studio.Login.HttpMessageHandlers;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Shell;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Elsa.Studio.Contracts;
using MagicPAI.Studio.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register root Blazor components
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Backend URL — Elsa API endpoints are at root (no /elsa/api prefix with FastEndpoints)
var backendUrl = builder.Configuration["Backend:Url"]
    ?? "http://localhost:5000";

// Elsa Studio services
builder.Services.AddCore();
builder.Services.AddShell();
builder.Services.AddRemoteBackend(new Elsa.Studio.Models.BackendApiConfig
{
    ConfigureHttpClientBuilder = options =>
        options.AuthenticationHandler = typeof(AuthenticatingApiHttpMessageHandler)
});
builder.Services.Configure<Elsa.Studio.Options.BackendOptions>(o => o.Url = new Uri(backendUrl));
builder.Services.AddLoginModule();
builder.Services.UseElsaIdentity();
builder.Services.AddDashboardModule();
builder.Services.AddWorkflowsModule();

// MagicPAI custom services
builder.Services.AddScoped<SessionHubClient>();
builder.Services.AddScoped<WorkflowInstanceLiveUpdater>();
builder.Services.AddScoped<SessionApiClient>(sp =>
{
    var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    return new SessionApiClient(httpClient);
});
builder.Services.AddScoped<IMenuProvider, MagicPaiMenuProvider>();

// Build and run startup tasks
var app = builder.Build();
var startupTaskRunner = app.Services.GetRequiredService<IStartupTaskRunner>();
await startupTaskRunner.RunStartupTasksAsync();
await app.RunAsync();
