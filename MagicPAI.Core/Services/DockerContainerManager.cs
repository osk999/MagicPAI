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

        return env;
    }

    public static IReadOnlyList<string> BuildCredentialBinds(string userProfile)
    {
        var binds = new List<string>();

        AddFileMount(".claude.json", "/tmp/magicpai-host-claude.json");
        AddFileMount(Path.Combine(".claude", ".credentials.json"), "/tmp/magicpai-host-claude-credentials.json");
        AddFileMount(Path.Combine(".codex", "auth.json"), "/tmp/magicpai-host-codex-auth.json");
        AddFileMount(Path.Combine(".codex", "cap_sid"), "/tmp/magicpai-host-codex-cap-sid");

        return binds;

        void AddFileMount(string sourceName, string target)
        {
            var source = Path.Combine(userProfile, sourceName);
            if (!File.Exists(source))
                return;

            binds.Add($"{source}:{target}:ro");
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
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(containerId);
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add(command);
        return psi;
    }

    private static ProcessStartInfo CreateDockerExecProcessStartInfo(string containerId, ContainerExecRequest request)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add(request.WorkingDirectory);
        }

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

    private static ProcessStartInfo CreateDockerCliStartInfo() =>
        new()
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
