using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Elsa.Studio.Core.BlazorWasm.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Extensions;
using MagicPAI.Studio.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var backendUrl = builder.Configuration["Elsa:Server:BaseUrl"]
    ?? "http://localhost:5000/elsa/api";

// Elsa Studio services
builder.Services.AddShell(options => options.DisableAuthorization = true);
builder.Services.AddSharedServices();
builder.Services.AddCore();
builder.Services.AddRemoteBackend(new Elsa.Studio.Models.BackendApiConfig());
builder.Services.AddWorkflowsModule();

// Configure backend URL
builder.Services.Configure<Elsa.Studio.Options.BackendOptions>(options =>
{
    options.Url = new Uri(backendUrl);
});

// MagicPAI custom services
builder.Services.AddScoped<SessionHubClient>();
builder.Services.AddScoped<SessionApiClient>(sp =>
{
    var httpClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    return new SessionApiClient(httpClient);
});

// Custom menu provider
builder.Services.AddScoped<Elsa.Studio.Contracts.IMenuProvider, MagicPaiMenuProvider>();

await builder.Build().RunAsync();
