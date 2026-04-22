using MagicPAI.Core.Services;
using MagicPAI.Tests.Integration.Stubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MagicPAI.Server.Services;
using Testcontainers.PostgreSql;

namespace MagicPAI.Tests.Integration.Fixtures;

/// <summary>
/// Custom WebApplicationFactory that configures MagicPAI.Server for testing.
/// Spins up a disposable PostgreSQL container via Testcontainers and
/// replaces external services (container manager, CLI agents) with stubs.
/// Temporal-only after Phase 3 — no Elsa services to override.
/// </summary>
public class MagicPaiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres;
    public StubContainerManager ContainerManager { get; } = new();
    public StubCliAgentFactory CliAgentFactory { get; } = new();

    public MagicPaiWebApplicationFactory()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("magicpai_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("MagicPAI:UseDocker", "true");
        builder.UseSetting("MagicPAI:RequireContainerizedAgentExecution", "true");
        builder.UseSetting("MagicPAI:EnableContainerGui", "true");
        builder.UseSetting("MagicPAI:EnableContainerPool", "false");
        builder.UseSetting("MagicPAI:EnableWorktreeIsolation", "false");

        // PostgreSQL from Testcontainer (Temporal persists its own state; the app's
        // connection string is kept for completeness even though Elsa is retired).
        builder.UseSetting("ConnectionStrings:MagicPai", _postgres.GetConnectionString());

        // Skip service validation so partially-configured Temporal worker services
        // don't crash startup when the real broker isn't reachable in unit suites.
        builder.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);

        builder.ConfigureServices(services =>
        {
            // Replace container manager with stub
            services.RemoveAll<IContainerManager>();
            services.AddSingleton<IContainerManager>(ContainerManager);

            // Replace CLI agent factory with stub
            services.RemoveAll<ICliAgentFactory>();
            services.AddSingleton<ICliAgentFactory>(CliAgentFactory);

            // Drop the long-running cleanup service so tests shut down cleanly.
            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType == typeof(WorkerPodGarbageCollector))
                .ToList();
            foreach (var d in hostedServiceDescriptors)
                services.Remove(d);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _postgres.DisposeAsync().GetAwaiter().GetResult();
    }
}
