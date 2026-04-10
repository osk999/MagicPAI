using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using MagicPAI.Server.Bridge;
using MagicPAI.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace MagicPAI.Tests.Integration.Workflows;

public class WorkflowTemplatePublishingIntegrationTests : IClassFixture<MagicPaiWebApplicationFactory>
{
    private readonly MagicPaiWebApplicationFactory _factory;

    public WorkflowTemplatePublishingIntegrationTests(MagicPaiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Publisher_Imports_All_Workflows_From_Json_Templates()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var templateDirectory = Path.Combine(environment.ContentRootPath, "Workflows", "Templates");

        var deadline = DateTime.UtcNow.AddSeconds(45);
        var lastState = "No workflow state collected.";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var allImported = true;
                var stateLines = new List<string>();

                foreach (var entry in WorkflowCatalog.Entries)
                {
                    var definitions = await store.FindManyAsync(new WorkflowDefinitionFilter
                    {
                        DefinitionId = entry.DefinitionId
                    });

                    var published = definitions
                        .Where(x => x.IsPublished)
                        .OrderByDescending(x => x.Version)
                        .FirstOrDefault();

                    var templatePath = Path.Combine(templateDirectory, entry.TemplateFileName);
                    var expectedSource = entry.UseJsonTemplate ? "JsonTemplate" : "CodeFirst";
                    var templateReady = !entry.UseJsonTemplate || File.Exists(templatePath);
                    var actualSource = published?.CustomProperties.TryGetValue("Source", out var sourceValue) == true
                        ? sourceValue?.ToString()
                        : "<missing>";

                    stateLines.Add($"{entry.DefinitionId}: expected={expectedSource}, actual={actualSource}, published={(published is not null)}, templateReady={templateReady}");

                    allImported &= published is not null && templateReady;

                    if (entry.UseJsonTemplate)
                    {
                        allImported &=
                            published is not null &&
                            published.CustomProperties.TryGetValue("Source", out var source) &&
                            string.Equals(source?.ToString(), expectedSource, StringComparison.Ordinal) &&
                            string.Equals(published.CustomProperties["TemplateFileName"]?.ToString(), entry.TemplateFileName, StringComparison.Ordinal);
                    }
                }

                lastState = string.Join(Environment.NewLine, stateLines);

                if (allImported)
                    return;
            }
            catch (PostgresException ex) when (ex.SqlState == "42703")
            {
                // Wait for WorkflowPublisher schema patch + sync.
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException(
            $"WorkflowPublisher did not finish JSON template import within 45 seconds.{Environment.NewLine}{lastState}");
    }
}
