using MagicPAI.Core.Services;
using MagicPAI.Tests.Integration.Stubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Elsa.Common.Services;
using Elsa.Tenants;
using Elsa.Labels.Contracts;
using Elsa.Labels.Entities;
using Elsa.Labels.Services;
using MagicPAI.Server.Bridge;
using MagicPAI.Server.Services;
using Testcontainers.PostgreSql;

namespace MagicPAI.Tests.Integration.Fixtures;

/// <summary>
/// Custom WebApplicationFactory that configures MagicPAI.Server for testing.
/// Spins up a disposable PostgreSQL container via Testcontainers and
/// replaces external services (container manager, CLI agents) with stubs.
/// </summary>
public class MagicPaiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres;
    public StubContainerManager ContainerManager { get; } = new();
    public StubCliAgentFactory CliAgentFactory { get; } = new();

    public MagicPaiWebApplicationFactory()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
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

        // PostgreSQL from Testcontainer
        builder.UseSetting("ConnectionStrings:MagicPai", _postgres.GetConnectionString());

        // Disable service validation so unregistered Elsa FastEndpoint dependencies don't crash startup
        builder.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);

        builder.ConfigureServices(services =>
        {
            // Replace container manager with stub
            services.RemoveAll<IContainerManager>();
            services.AddSingleton<IContainerManager>(ContainerManager);

            // Replace CLI agent factory with stub
            services.RemoveAll<ICliAgentFactory>();
            services.AddSingleton<ICliAgentFactory>(CliAgentFactory);

            services.RemoveAll<ITenantStore>();
            services.AddSingleton<ITenantStore, InMemoryTenantStore>();

            services.RemoveAll<ILabelStore>();
            services.RemoveAll<MemoryStore<Label>>();
            services.AddSingleton<MemoryStore<Label>>();
            services.AddSingleton<ILabelStore, InMemoryLabelStore>();

            // Keep WorkflowPublisher so real workflow definitions exist during integration tests.
            // Remove only the worker cleanup service, which is irrelevant here.
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
