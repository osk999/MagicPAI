using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class DockerContainerManager : IContainerManager, IDisposable
{
    private readonly DockerClient _docker;
    private readonly ConcurrentDictionary<string, string?> _guiUrls = new();
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    ];

    public DockerContainerManager()
    {
        _docker = new DockerClientConfiguration().CreateClient();
    }

    public DockerContainerManager(DockerClient docker)
    {
        _docker = docker;
    }

    public async Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
    {
        var hostConfig = new HostConfig
        {
            Memory = config.MemoryLimitMb * 1024L * 1024L,
            NanoCPUs = config.CpuCount * 1_000_000_000L,
            Binds = [$"{config.WorkspacePath}:{config.ContainerWorkDir}"]
        };

        if (config.MountDockerSocket)
            hostConfig.Binds.Add("/var/run/docker.sock:/var/run/docker.sock");

        var exposedPorts = new Dictionary<string, EmptyStruct>();
        var portBindings = new Dictionary<string, IList<PortBinding>>();

        if (config.EnableGui && config.GuiPort.HasValue)
        {
            exposedPorts["7900/tcp"] = default;
            portBindings["7900/tcp"] =
            [
                new PortBinding { HostPort = config.GuiPort.Value.ToString() }
            ];
            hostConfig.PortBindings = portBindings;
        }

        var createParams = new CreateContainerParameters
        {
            Image = config.Image,
            WorkingDir = config.ContainerWorkDir,
            Env = BuildEnv(config),
            ExposedPorts = exposedPorts,
            HostConfig = hostConfig,
            Tty = true,
            OpenStdin = true
        };

        var response = await RetryAsync(
            () => _docker.Containers.CreateContainerAsync(createParams, ct), ct);
        await RetryAsync(
            () => _docker.Containers.StartContainerAsync(response.ID,
                new ContainerStartParameters(), ct), ct);

        string? guiUrl = config.EnableGui && config.GuiPort.HasValue
            ? $"http://localhost:{config.GuiPort.Value}"
            : null;

        _guiUrls[response.ID] = guiUrl;
        return new ContainerInfo(response.ID, guiUrl);
    }

    public async Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct)
    {
        var execParams = new ContainerExecCreateParameters
        {
            Cmd = ["bash", "-c", command],
            AttachStdout = true,
            AttachStderr = true,
            WorkingDir = workDir
        };

        var exec = await _docker.Exec.ExecCreateContainerAsync(containerId, execParams, ct);
        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(exec.ID, false, ct);

        var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);

        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);
        return new ExecResult((int)inspect.ExitCode, stdout, stderr);
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var execParams = new ContainerExecCreateParameters
        {
            Cmd = ["bash", "-c", command],
            AttachStdout = true,
            AttachStderr = true
        };

        var exec = await _docker.Exec.ExecCreateContainerAsync(containerId, execParams, cts.Token);
        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cts.Token);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var buffer = new byte[4096];

        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cts.Token);
            if (result.EOF)
                break;

            var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (result.Target == MultiplexedStream.TargetStream.StandardOut)
            {
                stdout.Append(text);
                onOutput(text);
            }
            else
            {
                stderr.Append(text);
            }
        }

        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, cts.Token);
        return new ExecResult((int)inspect.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task DestroyAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, ct);
        }
        catch (DockerApiException)
        {
            // Container may already be stopped or removed
        }
        catch (OperationCanceledException)
        {
            // Cancellation during stop is acceptable
        }

        await _docker.Containers.RemoveContainerAsync(containerId,
            new ContainerRemoveParameters { Force = true }, ct);

        _guiUrls.TryRemove(containerId, out _);
    }

    public async Task<bool> IsRunningAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var inspect = await _docker.Containers.InspectContainerAsync(containerId, ct);
            return inspect.State.Running;
        }
        catch
        {
            return false;
        }
    }

    public string? GetGuiUrl(string containerId) =>
        _guiUrls.TryGetValue(containerId, out var url) ? url : null;

    /// <summary>
    /// Build container env vars, auto-injecting host API keys for CLI agents.
    /// </summary>
    private static List<string> BuildEnv(ContainerConfig config)
    {
        var env = config.Env.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        // Auto-inject API keys from host if not already set
        string[] autoPassKeys = ["ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", "GOOGLE_API_KEY"];
        foreach (var key in autoPassKeys)
        {
            if (config.Env.ContainsKey(key)) continue;
            var val = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(val))
                env.Add($"{key}={val}");
        }

        return env;
    }

    public void Dispose()
    {
        _docker.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Retry a Docker operation with exponential backoff for transient failures.</summary>
    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception) when (attempt < MaxRetries - 1 && !ct.IsCancellationRequested)
            {
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    /// <summary>Retry a Docker operation (void return) with exponential backoff.</summary>
    private static async Task RetryAsync(Func<Task> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception) when (attempt < MaxRetries - 1 && !ct.IsCancellationRequested)
            {
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }
}
