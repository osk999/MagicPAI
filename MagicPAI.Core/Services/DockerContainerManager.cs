using System.Collections.Concurrent;
using System.Diagnostics;
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
        Trace.WriteLine($"[MagicPAI] docker create starting for image={config.Image} workspace={config.WorkspacePath}");
        var createResult = await RunDockerAsync(
            ConfigureCreateStartInfo(config),
            ct);

        var containerId = createResult.stdout.Trim();
        if (string.IsNullOrWhiteSpace(containerId))
            throw new InvalidOperationException(
                $"docker create returned no container id. stderr: {createResult.stderr}");

        try
        {
            Trace.WriteLine($"[MagicPAI] docker start starting for container={containerId}");
            await RunDockerAsync(
                ConfigureStartStartInfo(containerId),
                ct);
            Trace.WriteLine($"[MagicPAI] docker start finished for container={containerId}");
        }
        catch
        {
            await SafeRemoveContainerAsync(containerId, ct);
            throw;
        }

        string? guiUrl = config.EnableGui && config.GuiPort.HasValue
            ? $"http://127.0.0.1:{config.GuiPort.Value}/vnc.html?autoconnect=1&resize=scale"
            : null;

        _guiUrls[containerId] = guiUrl;
        return new ContainerInfo(containerId, guiUrl);
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

    public async Task<ExecResult> ExecAsync(string containerId, ContainerExecRequest request,
        CancellationToken ct)
    {
        var psi = CreateDockerExecProcessStartInfo(containerId, request);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker exec process");

        if (request.StdinInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StdinInput.AsMemory(), ct);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ExecResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        var idleTimeout = ExecutionTimeoutPolicy.NormalizeIdleTimeout(timeout);
        var hardTimeout = ExecutionTimeoutPolicy.GetHardTimeout(idleTimeout);

        const int maxOutputBytes = 50 * 1024 * 1024; // 50MB cap
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var totalBytes = 0;
        var lastActivity = DateTimeOffset.UtcNow;

        void HandleChunk(StringBuilder target, string chunk)
        {
            if (chunk.Length == 0)
                return;

            lastActivity = DateTimeOffset.UtcNow;
            totalBytes += Encoding.UTF8.GetByteCount(chunk);
            if (totalBytes > maxOutputBytes)
                return;

            target.Append(chunk);
            onOutput(chunk);
        }

        var psi = CreateDockerExecProcessStartInfo(containerId, command);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker exec process");
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stdoutTask = ReadStreamAsync(
            process.StandardOutput,
            chunk => HandleChunk(stdout, chunk),
            readCts.Token);

        var stderrTask = ReadStreamAsync(
            process.StandardError,
            chunk => HandleChunk(stderr, chunk),
            readCts.Token);

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            while (true)
            {
                if (process.HasExited)
                    break;

                if (ct.IsCancellationRequested)
                {
                    await KillProcessTreeAsync(process);
                    ct.ThrowIfCancellationRequested();
                }

                if (DateTimeOffset.UtcNow - startedAt >= hardTimeout)
                {
                    await KillProcessTreeAsync(process);
                    stderr.AppendLine(ExecutionTimeoutPolicy.FormatHardTimeoutMessage(hardTimeout));
                    break;
                }

                if (DateTimeOffset.UtcNow - lastActivity >= idleTimeout)
                {
                    await KillProcessTreeAsync(process);
                    stderr.AppendLine(ExecutionTimeoutPolicy.FormatIdleTimeoutMessage(idleTimeout));
                    break;
                }

                if (totalBytes > maxOutputBytes)
                {
                    stderr.AppendLine("Command output exceeded the 50 MB capture limit. Truncating remaining output.");
                    await KillProcessTreeAsync(process);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            }

            readCts.Cancel();
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            return new ExecResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            await KillProcessTreeAsync(process);
            readCts.Cancel();
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            throw;
        }
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, ContainerExecRequest request,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        var idleTimeout = ExecutionTimeoutPolicy.NormalizeIdleTimeout(timeout);
        var hardTimeout = ExecutionTimeoutPolicy.GetHardTimeout(idleTimeout);

        const int maxOutputBytes = 50 * 1024 * 1024;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var totalBytes = 0;
        var lastActivity = DateTimeOffset.UtcNow;

        void HandleChunk(StringBuilder target, string chunk)
        {
            if (chunk.Length == 0)
                return;

            lastActivity = DateTimeOffset.UtcNow;
            totalBytes += Encoding.UTF8.GetByteCount(chunk);
            if (totalBytes > maxOutputBytes)
                return;

            target.Append(chunk);
            onOutput(chunk);
        }

        var psi = CreateDockerExecProcessStartInfo(containerId, request);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker exec process");
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Pipe oversized prompts / inputs through stdin when the request asks
        // for it (argv caps at ~32 KB on Windows, higher on Linux). Writing
        // sync on this path is fine — payloads are bounded at a few hundred
        // KB and we do it once before the read loop begins.
        if (request.StdinInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StdinInput.AsMemory(), ct);
            process.StandardInput.Close();
        }

        var stdoutTask = ReadStreamAsync(
            process.StandardOutput,
            chunk => HandleChunk(stdout, chunk),
            readCts.Token);

        var stderrTask = ReadStreamAsync(
            process.StandardError,
            chunk => HandleChunk(stderr, chunk),
            readCts.Token);

        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            while (true)
            {
                if (process.HasExited)
                    break;

                if (ct.IsCancellationRequested)
                {
                    await KillProcessTreeAsync(process);
                    ct.ThrowIfCancellationRequested();
                }

                if (DateTimeOffset.UtcNow - startedAt >= hardTimeout)
                {
                    await KillProcessTreeAsync(process);
                    stderr.AppendLine(ExecutionTimeoutPolicy.FormatHardTimeoutMessage(hardTimeout));
                    break;
                }

                if (DateTimeOffset.UtcNow - lastActivity >= idleTimeout)
                {
                    await KillProcessTreeAsync(process);
                    stderr.AppendLine(ExecutionTimeoutPolicy.FormatIdleTimeoutMessage(idleTimeout));
                    break;
                }

                if (totalBytes > maxOutputBytes)
                {
                    stderr.AppendLine("Command output exceeded the 50 MB capture limit. Truncating remaining output.");
                    await KillProcessTreeAsync(process);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            }

            readCts.Cancel();
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            return new ExecResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            await KillProcessTreeAsync(process);
            readCts.Cancel();
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            throw;
        }
    }

    public async Task DestroyAsync(string containerId, CancellationToken ct)
    {
        await SafeRemoveContainerAsync(containerId, ct);
        _guiUrls.TryRemove(containerId, out _);
    }

    public async Task<bool> IsRunningAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var (stdout, _) = await RunDockerAsync(
                ConfigureInspectRunningStartInfo(containerId),
                ct);
            return string.Equals(stdout.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string? GetGuiUrl(string containerId) =>
        _guiUrls.TryGetValue(containerId, out var url) ? url : null;

    public async Task<IReadOnlyList<LabeledContainer>> ListContainersByLabelAsync(
        string labelKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(labelKey))
            return [];

        // Use Docker.DotNet directly here — it's the cleanest way to get back
        // structured label dictionaries plus created-at timestamps in a single
        // round-trip. Spawn/destroy still go via the docker CLI to preserve
        // the rest of the working pipeline.
        var parameters = new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [labelKey] = true }
            }
        };

        IList<ContainerListResponse> response;
        try
        {
            response = await _docker.Containers.ListContainersAsync(parameters, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a Docker API hiccup must NOT take down the GC loop.
            // The caller logs and skips this scan iteration.
            Trace.WriteLine($"[MagicPAI] ListContainersByLabelAsync failed: {ex.Message}");
            return [];
        }

        var results = new List<LabeledContainer>(response.Count);
        foreach (var c in response)
        {
            var labels = (IReadOnlyDictionary<string, string>)
                (c.Labels is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(c.Labels, StringComparer.Ordinal));

            // Docker.DotNet exposes Created as DateTime (UTC) in recent versions.
            // Treat anything non-UTC as UTC defensively.
            var createdUtc = c.Created.Kind == DateTimeKind.Utc
                ? c.Created
                : DateTime.SpecifyKind(c.Created, DateTimeKind.Utc);

            // ContainerListResponse.State examples: "running", "exited", "created", "paused".
            var isRunning = string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase);

            results.Add(new LabeledContainer(c.ID, labels, createdUtc, isRunning));
        }

        return results;
    }

    public async Task StreamLogsAsync(string containerId, Action<string> onLog, CancellationToken ct)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("logs");
        psi.ArgumentList.Add("--follow");
        psi.ArgumentList.Add(containerId);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker logs process");

        var stdoutTask = ReadLogLinesAsync(process.StandardOutput, onLog, ct);
        var stderrTask = ReadLogLinesAsync(process.StandardError, onLog, ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
        }
    }

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

        // Disable Claude CLI hooks inside containers (they reference host commands like 'cmd')
        env.Add("DISABLE_HOOKS=1");
        env.Add("HOME=/home/worker");
        env.Add("XDG_CONFIG_HOME=/home/worker/.config");

        // Playwright: headed mode on DISPLAY=:99 for noVNC visibility
        env.Add("DISPLAY=:99");
        env.Add("PLAYWRIGHT_BROWSERS_PATH=/ms-playwright");
        env.Add("PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1");
        env.Add("PLAYWRIGHT_CHROMIUM_HEADLESS=0");
        env.Add("PLAYWRIGHT_MCP_HEADLESS=false");
        env.Add("HEADED=1");

        return env;
    }

    public static IReadOnlyList<string> BuildCredentialBinds(string userProfile)
    {
        // When the server runs inside a container, the path emitted to the
        // worker `docker run -v` must be a HOST path (the daemon resolves it
        // on the host), but `File.Exists` must probe the path *visible to the
        // server*. MAGICPAI_HOST_USERPROFILE lets ops mount the host's
        // ~/.claude / ~/.codex into the server at `userProfile` while emitting
        // the host-side path to worker binds.
        var hostUserProfile = Environment.GetEnvironmentVariable("MAGICPAI_HOST_USERPROFILE");
        return BuildCredentialBinds(userProfile, hostUserProfile);
    }

    public static IReadOnlyList<string> BuildCredentialBinds(string userProfile, string? hostUserProfile)
    {
        var binds = new List<string>();
        var hostRoot = string.IsNullOrWhiteSpace(hostUserProfile) ? userProfile : hostUserProfile;

        AddFileMount(".claude.json", "/tmp/magicpai-host-claude.json");
        AddFileMount(Path.Combine(".claude", ".credentials.json"), "/tmp/magicpai-host-claude-credentials.json");
        AddFileMount(Path.Combine(".codex", "auth.json"), "/tmp/magicpai-host-codex-auth.json");
        AddFileMount(Path.Combine(".codex", "cap_sid"), "/tmp/magicpai-host-codex-cap-sid");

        return binds;

        void AddFileMount(string sourceName, string target)
        {
            var probe = Path.Combine(userProfile, sourceName);
            if (!File.Exists(probe))
                return;

            // Host paths may contain backslashes on Windows; docker -v wants
            // forward slashes for the source on Windows daemons.
            var hostSource = Path.Combine(hostRoot, sourceName).Replace('\\', '/');
            binds.Add($"{hostSource}:{target}:ro");
        }
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

    private static ProcessStartInfo CreateDockerExecProcessStartInfo(string containerId, string command)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(containerId);
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add(command);
        return psi;
    }

    private static ProcessStartInfo CreateDockerExecProcessStartInfo(string containerId, ContainerExecRequest request)
    {
        var psi = CreateDockerCliStartInfo();

        psi.ArgumentList.Add("exec");

        // When the caller wants to pipe data to stdin we need `docker exec -i`
        // (keep STDIN open) AND tell .NET to redirect stdin on the host.
        if (request.StdinInput is not null)
        {
            psi.ArgumentList.Add("-i");
            psi.RedirectStandardInput = true;
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add(request.WorkingDirectory);
        }

        // Always inject DISPLAY + Playwright env for headed browser support
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("DISPLAY=:99");
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("PLAYWRIGHT_BROWSERS_PATH=/ms-playwright");
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("PLAYWRIGHT_CHROMIUM_HEADLESS=0");
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("PLAYWRIGHT_MCP_HEADLESS=false");
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1");

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(pair.Value is null ? pair.Key : $"{pair.Key}={pair.Value}");
            }
        }

        psi.ArgumentList.Add(containerId);
        psi.ArgumentList.Add(request.FileName);

        foreach (var argument in request.Arguments)
            psi.ArgumentList.Add(argument);

        return psi;
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onChunk,
        CancellationToken ct)
    {
        var buffer = new char[256];

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer, ct);
                if (read == 0)
                    break;

                onChunk(new string(buffer, 0, read));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task ReadLogLinesAsync(StreamReader reader, Action<string> onLog,
        CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                    break;

                onLog(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task DrainStreamTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitForProcessExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        await WaitForProcessExitAsync(process);
    }

    private static ProcessStartInfo ConfigureCreateStartInfo(ContainerConfig config)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("create");
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add("1000:1000");
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add(config.ContainerWorkDir);
        psi.ArgumentList.Add("--memory");
        psi.ArgumentList.Add($"{config.MemoryLimitMb}m");
        psi.ArgumentList.Add("--cpus");
        psi.ArgumentList.Add(config.CpuCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add($"{config.WorkspacePath}:{config.ContainerWorkDir}");

        if (config.MountDockerSocket)
        {
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("/var/run/docker.sock:/var/run/docker.sock");
        }

        foreach (var credentialBind in BuildCredentialBinds(
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
        {
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add(credentialBind);
        }

        foreach (var envVar in BuildEnv(config))
        {
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(envVar);
        }

        // Attach session/workflow labels so the GC can identify MagicPAI-owned
        // containers across server restarts even when the in-memory tracker
        // is gone. See WorkerPodGarbageCollector fallback sweep.
        foreach (var label in config.Labels)
        {
            if (string.IsNullOrWhiteSpace(label.Key))
                continue;

            psi.ArgumentList.Add("--label");
            psi.ArgumentList.Add($"{label.Key}={label.Value ?? string.Empty}");
        }

        if (config.EnableGui && config.GuiPort.HasValue)
        {
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add($"127.0.0.1:{config.GuiPort.Value}:7900");
        }

        psi.ArgumentList.Add(config.Image);
        return psi;
    }

    private static ProcessStartInfo ConfigureStartStartInfo(string containerId)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("start");
        psi.ArgumentList.Add(containerId);
        return psi;
    }

    private static ProcessStartInfo ConfigureInspectRunningStartInfo(string containerId)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("inspect");
        psi.ArgumentList.Add(containerId);
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("{{.State.Running}}");
        return psi;
    }

    private static ProcessStartInfo ConfigureRemoveStartInfo(string containerId)
    {
        var psi = CreateDockerCliStartInfo();
        psi.ArgumentList.Add("rm");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(containerId);
        return psi;
    }

    private static ProcessStartInfo CreateDockerCliStartInfo()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Prevent MSYS/Git Bash from converting Linux paths (e.g. /workspace)
        // to Windows paths (e.g. C:/Program Files/Git/workspace) when running
        // Docker CLI commands on Windows.
        if (OperatingSystem.IsWindows())
        {
            psi.Environment["MSYS_NO_PATHCONV"] = "1";
            psi.Environment["MSYS2_ARG_CONV_EXCL"] = "*";
        }

        return psi;
    }

    private static async Task<(string stdout, string stderr)> RunDockerAsync(
        ProcessStartInfo psi,
        CancellationToken ct)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"docker command failed with exit code {process.ExitCode}: {stderr}");

        return (stdout, stderr);
    }

    private static async Task SafeRemoveContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await RunDockerAsync(ConfigureRemoveStartInfo(containerId), ct);
        }
        catch (InvalidOperationException)
        {
            // Container may already be removed.
        }
    }
}
