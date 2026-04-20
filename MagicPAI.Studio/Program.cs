// MagicPAI.Studio/Program.cs
// Temporal migration §10.5: MudBlazor-only Studio host.
// - No Elsa.Studio.* packages.
// - HttpClient bound to backend URL resolved via BackendUrlResolver.
// - Thin SignalR client (SessionHubClient) wraps /hub.
// - MudBlazor renders all pages.
using MagicPAI.Studio;
using MagicPAI.Studio.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var backendUri = BackendUrlResolver.ResolveBackendUri(
    builder.Configuration,
    builder.HostEnvironment);

// ── HTTP clients ────────────────────────────────────────────────────────
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = backendUri });
builder.Services.AddScoped<SessionApiClient>();
builder.Services.AddScoped<WorkflowCatalogClient>();
builder.Services.AddScoped<TemporalUiUrlBuilder>();

// ── SignalR client ──────────────────────────────────────────────────────
builder.Services.AddScoped<SessionHubClient>();

// ── UI ──────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

var app = builder.Build();
await app.RunAsync();
