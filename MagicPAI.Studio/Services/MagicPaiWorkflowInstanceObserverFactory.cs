using System.Net;
using Elsa.Api.Client.Resources.ActivityExecutions.Contracts;
using Elsa.Api.Client.Resources.WorkflowInstances.Contracts;
using Elsa.Studio.Contracts;
using Elsa.Studio.Workflows.Contracts;
using Elsa.Studio.Workflows.Models;
using Elsa.Studio.Workflows.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MagicPAI.Studio.Services;

/// <summary>
/// MagicPAI-specific workflow observer factory that authenticates SignalR
/// requests with the Elsa admin API key used by the embedded studio.
/// </summary>
public sealed class MagicPaiWorkflowInstanceObserverFactory(
    IBackendApiClientProvider backendApiClientProvider,
    IRemoteFeatureProvider remoteFeatureProvider,
    ILogger<MagicPaiWorkflowInstanceObserverFactory> logger,
    ElsaStudioConnectionSettings settings) : IWorkflowInstanceObserverFactory
{
    public Task<IWorkflowInstanceObserver> CreateAsync(string workflowInstanceId)
    {
        var context = new WorkflowInstanceObserverContext
        {
            WorkflowInstanceId = workflowInstanceId
        };

        return CreateAsync(context);
    }

    public async Task<IWorkflowInstanceObserver> CreateAsync(WorkflowInstanceObserverContext context)
    {
        var cancellationToken = context.CancellationToken;
        var workflowInstancesApi = await backendApiClientProvider.GetApiAsync<IWorkflowInstancesApi>(cancellationToken);
        var activityExecutionsApi = await backendApiClientProvider.GetApiAsync<IActivityExecutionsApi>(cancellationToken);

        if (!await remoteFeatureProvider.IsEnabledAsync("Elsa.RealTimeWorkflowUpdates", cancellationToken))
            return CreatePollingObserver(context, workflowInstancesApi, activityExecutionsApi);

        var hubUrl = new Uri(backendApiClientProvider.Url, "hubs/workflow-instance").ToString();
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Authorization"] = $"ApiKey {settings.ApiKey}";
            })
            .Build();

        var observer = new SignalRWorkflowInstanceObserver(connection);

        try
        {
            await connection.StartAsync(cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                e,
                "Workflow instance observer hub connection failed with status code {StatusCode}. Falling back to polling observer",
                e.StatusCode);
            return CreatePollingObserver(context, workflowInstancesApi, activityExecutionsApi);
        }

        await connection.SendAsync("ObserveInstanceAsync", context.WorkflowInstanceId, cancellationToken: cancellationToken);
        return observer;
    }

    private static PollingWorkflowInstanceObserver CreatePollingObserver(
        WorkflowInstanceObserverContext context,
        IWorkflowInstancesApi workflowInstancesApi,
        IActivityExecutionsApi activityExecutionsApi)
    {
        return new PollingWorkflowInstanceObserver(
            context,
            workflowInstancesApi,
            activityExecutionsApi);
    }
}
