using System.Collections.Concurrent;
using System.Text;
using k8s;
using k8s.Models;
using MagicPAI.Core.Config;
using MagicPAI.Core.K8s;
using MagicPAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace MagicPAI.Core.Services;

/// <summary>
/// Implements <see cref="IContainerManager"/> using the Kubernetes API.
/// Worker pods are created from <see cref="WorkerPodTemplate"/> and commands
/// are executed via the K8s exec WebSocket protocol.
/// </summary>
public class KubernetesContainerManager : IContainerManager, IDisposable
{
    private readonly IKubernetes _client;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<KubernetesContainerManager> _logger;
    private readonly string _namespace;
    private readonly ConcurrentDictionary<string, string?> _guiUrls = new();
    private readonly ConcurrentDictionary<string, string> _podNames = new();

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];

    private static readonly TimeSpan PodReadyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PodPollInterval = TimeSpan.FromSeconds(2);

    public KubernetesContainerManager(MagicPaiConfig config, ILogger<KubernetesContainerManager> logger)
    {
        _config = config;
        _logger = logger;
        _namespace = config.KubernetesNamespace;

        try
        {
            var k8sConfig = KubernetesClientConfiguration.InClusterConfig();
            _client = new Kubernetes(k8sConfig);
            _logger.LogInformation("Kubernetes client initialized using in-cluster configuration");
        }
        catch (Exception)
        {
            var k8sConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile();
            _client = new Kubernetes(k8sConfig);
            _logger.LogInformation("Kubernetes client initialized using kubeconfig file");
        }
    }

    /// <summary>
    /// Constructor for testing — accepts a pre-built Kubernetes client.
    /// </summary>
    public KubernetesContainerManager(IKubernetes client, MagicPaiConfig config,
        ILogger<KubernetesContainerManager> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
        _namespace = config.KubernetesNamespace;
    }

    public async Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var podTemplate = WorkerPodTemplate.Create(config, instanceId, _namespace);
        var podName = podTemplate.Metadata.Name;

        _logger.LogInformation("Creating worker pod {PodName} in namespace {Namespace}",
            podName, _namespace);

        var createdPod = await RetryAsync(
            () => _client.CoreV1.CreateNamespacedPodAsync(podTemplate, _namespace, cancellationToken: ct),
            ct);

        // Wait for pod to reach Running phase
        await WaitForPodRunningAsync(podName, ct);

        string? guiUrl = config.EnableGui
            ? await ResolveGuiUrlAsync(podName, ct)
            : null;

        // Use the pod name as the container ID for K8s
        _podNames[podName] = podName;
        _guiUrls[podName] = guiUrl;

        _logger.LogInformation("Worker pod {PodName} is running", podName);
        return new ContainerInfo(podName, guiUrl);
    }

    public async Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct)
    {
        var podName = containerId;
        var fullCommand = string.IsNullOrEmpty(workDir)
            ? command
            : $"cd {workDir} && {command}";

        _logger.LogDebug("Executing in pod {PodName}: {Command}", podName, fullCommand);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = 0;

        try
        {
            var webSocket = await _client.WebSocketNamespacedPodExecAsync(
                name: podName,
                @namespace: _namespace,
                command: ["bash", "-c", fullCommand],
                container: "worker",
                stderr: true,
                stdin: false,
                stdout: true,
                tty: false,
                cancellationToken: ct);

            using var demuxer = new StreamDemuxer(webSocket);
            demuxer.Start();

            using var stdoutStream = demuxer.GetStream(1, null);
            using var stderrStream = demuxer.GetStream(2, null);

            var stdoutTask = ReadStreamToStringAsync(stdoutStream, ct);
            var stderrTask = ReadStreamToStringAsync(stderrStream, ct);

            var results = await Task.WhenAll(stdoutTask, stderrTask);
            stdout.Append(results[0]);
            stderr.Append(results[1]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error executing command in pod {PodName}", podName);
            stderr.Append(ex.Message);
            exitCode = 1;
        }

        return new ExecResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var podName = containerId;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = 0;

        _logger.LogDebug("Streaming exec in pod {PodName}: {Command}", podName, command);

        try
        {
            var webSocket = await _client.WebSocketNamespacedPodExecAsync(
                name: podName,
                @namespace: _namespace,
                command: ["bash", "-c", command],
                container: "worker",
                stderr: true,
                stdin: false,
                stdout: true,
                tty: false,
                cancellationToken: cts.Token);

            using var demuxer = new StreamDemuxer(webSocket);
            demuxer.Start();

            using var stdoutStream = demuxer.GetStream(1, null);
            using var stderrStream = demuxer.GetStream(2, null);

            // Read stderr in the background
            var stderrTask = ReadStreamToStringAsync(stderrStream, cts.Token);

            // Stream stdout with callback
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await stdoutStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                stdout.Append(text);
                onOutput(text);
            }

            stderr.Append(await stderrTask);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Command timed out after {Timeout} in pod {PodName}", timeout, podName);
            stderr.Append($"Command timed out after {timeout}");
            exitCode = 124; // Standard timeout exit code
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during streaming exec in pod {PodName}", podName);
            stderr.Append(ex.Message);
            exitCode = 1;
        }

        return new ExecResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task DestroyAsync(string containerId, CancellationToken ct)
    {
        var podName = containerId;
        _logger.LogInformation("Destroying worker pod {PodName}", podName);

        try
        {
            await _client.CoreV1.DeleteNamespacedPodAsync(
                podName, _namespace,
                gracePeriodSeconds: 5,
                cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Pod {PodName} not found during destroy — may already be deleted", podName);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cancellation during pod destroy for {PodName}", podName);
        }

        _podNames.TryRemove(podName, out _);
        _guiUrls.TryRemove(podName, out _);
    }

    public async Task<bool> IsRunningAsync(string containerId, CancellationToken ct)
    {
        var podName = containerId;
        try
        {
            var pod = await _client.CoreV1.ReadNamespacedPodAsync(podName, _namespace, cancellationToken: ct);
            return pod.Status?.Phase == "Running";
        }
        catch
        {
            return false;
        }
    }

    public string? GetGuiUrl(string containerId) =>
        _guiUrls.TryGetValue(containerId, out var url) ? url : null;

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    // --- Private helpers ---

    private async Task WaitForPodRunningAsync(string podName, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PodReadyTimeout);

        _logger.LogDebug("Waiting for pod {PodName} to reach Running phase", podName);

        while (!cts.Token.IsCancellationRequested)
        {
            var pod = await _client.CoreV1.ReadNamespacedPodAsync(podName, _namespace,
                cancellationToken: cts.Token);

            var phase = pod.Status?.Phase;
            switch (phase)
            {
                case "Running":
                    return;
                case "Failed":
                case "Unknown":
                    throw new InvalidOperationException(
                        $"Pod {podName} entered {phase} phase. " +
                        $"Reason: {pod.Status?.Reason ?? "unknown"}");
                default:
                    // Pending — keep waiting
                    await Task.Delay(PodPollInterval, cts.Token);
                    break;
            }
        }

        throw new TimeoutException(
            $"Pod {podName} did not reach Running phase within {PodReadyTimeout}");
    }

    private async Task<string?> ResolveGuiUrlAsync(string podName, CancellationToken ct)
    {
        // Try to find a NodePort service or use port-forward URL pattern
        try
        {
            var services = await _client.CoreV1.ListNamespacedServiceAsync(
                _namespace,
                labelSelector: $"app=magicpai-worker,workflow-instance={podName}",
                cancellationToken: ct);

            var svc = services.Items.FirstOrDefault();
            if (svc?.Spec.Ports != null)
            {
                var guiPort = svc.Spec.Ports.FirstOrDefault(p => p.Name == "novnc");
                if (guiPort?.NodePort != null)
                {
                    return $"http://localhost:{guiPort.NodePort}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve GUI URL for pod {PodName}", podName);
        }

        // Fallback: assume port-forward will be set up externally
        return null;
    }

    private static async Task<string> ReadStreamToStringAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];
        int bytesRead;

        try
        {
            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            }
        }
        catch (OperationCanceledException)
        {
            // Stream closed or cancelled — return what we have
        }
        catch (IOException)
        {
            // Stream may be closed by the remote end
        }

        return sb.ToString();
    }

    /// <summary>Retry a K8s operation with exponential backoff for transient failures.</summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < MaxRetries - 1 && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "K8s operation failed (attempt {Attempt}/{MaxRetries}), retrying...",
                    attempt + 1, MaxRetries);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }
}
