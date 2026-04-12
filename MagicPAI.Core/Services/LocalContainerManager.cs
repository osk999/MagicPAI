using System.Diagnostics;
using MagicPAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace MagicPAI.Core.Services;

/// <summary>
/// IContainerManager implementation that runs commands directly on the host
/// instead of inside Docker containers. Used when UseDocker=false for local
/// development and testing without Docker.
///
/// "Containers" are simulated — SpawnAsync returns a fake container ID,
/// ExecAsync runs commands via local shell, DestroyAsync is a no-op.
/// </summary>
public class LocalContainerManager : IContainerManager
{
    private readonly ILogger<LocalContainerManager> _logger;
    private readonly HashSet<string> _activeContainers = new();

    public LocalContainerManager(ILogger<LocalContainerManager> logger)
    {
        _logger = logger;
    }

    public Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
    {
        var containerId = $"local-{Guid.NewGuid():N}"[..20];
        _activeContainers.Add(containerId);

        _logger.LogInformation(
            "Local mode: Simulated container {ContainerId} (workspace={Workspace})",
            containerId, config.WorkspacePath);

        return Task.FromResult(new ContainerInfo(containerId, null));
    }

    public async Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct)
    {
        _logger.LogInformation("Local exec in {WorkDir}: {Command}", workDir, command[..Math.Min(200, command.Length)]);

        var psi = CreateProcessStartInfo(command, workDir);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        // 5 minute timeout for non-streaming exec
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            _logger.LogInformation("Local exec completed: exit={ExitCode}, output={Length}chars",
                process.ExitCode, output.Length);

            return new ExecResult(process.ExitCode, output, stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            _logger.LogWarning("Local exec timed out after 5 minutes");
            return new ExecResult(-1, "", "Process timed out");
        }
    }

    public async Task<ExecResult> ExecAsync(string containerId, ContainerExecRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("Local exec in {WorkDir}: {FileName}", request.WorkingDirectory, request.FileName);

        var psi = CreateProcessStartInfo(request);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return new ExecResult(process.ExitCode, output, stderr);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return new ExecResult(-1, "", "Process timed out");
        }
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogDebug("Local exec (streaming): {Command}", command);

        var psi = CreateProcessStartInfo(command, ".");
        psi.RedirectStandardInput = false;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        var idleTimeout = ExecutionTimeoutPolicy.NormalizeIdleTimeout(timeout);
        var hardTimeout = ExecutionTimeoutPolicy.GetHardTimeout(idleTimeout);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(hardTimeout);

        var outputBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();
        var lastActivity = DateTimeOffset.UtcNow;

        void RecordActivity() => lastActivity = DateTimeOffset.UtcNow;

        var stdoutTask = ReadStreamAsync(
            process.StandardOutput,
            chunk =>
            {
                RecordActivity();
                outputBuilder.Append(chunk);
                onOutput(chunk);
            },
            cts.Token);

        var stderrTask = ReadStreamAsync(
            process.StandardError,
            chunk =>
            {
                RecordActivity();
                stderrBuilder.Append(chunk);
                onOutput(chunk);
            },
            cts.Token);

        var exitTask = process.WaitForExitAsync(cts.Token);

        try
        {
            while (true)
            {
                var completedTask = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(1)));

                if (completedTask == exitTask)
                    break;

                cts.Token.ThrowIfCancellationRequested();
                ExecutionTimeoutPolicy.ThrowIfIdle(lastActivity, idleTimeout);
            }

            await exitTask;
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            return new ExecResult(process.ExitCode, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (IdleCommandTimeoutException ex)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            stderrBuilder.AppendLine(ex.Message);
            return new ExecResult(124, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            stderrBuilder.AppendLine(ExecutionTimeoutPolicy.FormatHardTimeoutMessage(hardTimeout));
            return new ExecResult(124, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            throw;
        }
    }

    public async Task<ExecResult> ExecStreamingAsync(string containerId, ContainerExecRequest request,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogDebug("Local exec (streaming): {FileName}", request.FileName);

        var psi = CreateProcessStartInfo(request);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        var idleTimeout = ExecutionTimeoutPolicy.NormalizeIdleTimeout(timeout);
        var hardTimeout = ExecutionTimeoutPolicy.GetHardTimeout(idleTimeout);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(hardTimeout);

        var outputBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();
        var lastActivity = DateTimeOffset.UtcNow;

        void RecordActivity() => lastActivity = DateTimeOffset.UtcNow;

        var stdoutTask = ReadStreamAsync(
            process.StandardOutput,
            chunk =>
            {
                RecordActivity();
                outputBuilder.Append(chunk);
                onOutput(chunk);
            },
            cts.Token);

        var stderrTask = ReadStreamAsync(
            process.StandardError,
            chunk =>
            {
                RecordActivity();
                stderrBuilder.Append(chunk);
                onOutput(chunk);
            },
            cts.Token);

        var exitTask = process.WaitForExitAsync(cts.Token);

        try
        {
            while (true)
            {
                var completedTask = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(1)));

                if (completedTask == exitTask)
                    break;

                cts.Token.ThrowIfCancellationRequested();
                ExecutionTimeoutPolicy.ThrowIfIdle(lastActivity, idleTimeout);
            }

            await exitTask;
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            return new ExecResult(process.ExitCode, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (IdleCommandTimeoutException ex)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            stderrBuilder.AppendLine(ex.Message);
            return new ExecResult(124, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            await DrainStreamTaskAsync(stdoutTask);
            await DrainStreamTaskAsync(stderrTask);
            stderrBuilder.AppendLine(ExecutionTimeoutPolicy.FormatHardTimeoutMessage(hardTimeout));
            return new ExecResult(124, outputBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await WaitForProcessExitAsync(process);
            throw;
        }
    }

    public Task DestroyAsync(string containerId, CancellationToken ct)
    {
        _activeContainers.Remove(containerId);
        _logger.LogInformation("Local mode: Released container {ContainerId}", containerId);
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(string containerId, CancellationToken ct)
    {
        return Task.FromResult(_activeContainers.Contains(containerId));
    }

    public Task StreamLogsAsync(string containerId, Action<string> onLog, CancellationToken ct) =>
        Task.CompletedTask;

    public string? GetGuiUrl(string containerId) => null;

    private static ProcessStartInfo CreateProcessStartInfo(string command, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            // Use Arguments string (not ArgumentList) to avoid auto-quoting
            // which breaks cmd.exe's parsing of && operators
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c {command}";
        }
        else
        {
            psi.FileName = "bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return psi;
    }

    private static ProcessStartInfo CreateProcessStartInfo(ContainerExecRequest request)
    {
        // On Windows, npm global scripts are .cmd files (e.g., codex.cmd).
        // Process.Start with UseShellExecute=false can't find .cmd files,
        // so we try the .cmd variant if the original FileName isn't found.
        var fileName = request.FileName;
        if (OperatingSystem.IsWindows() &&
            !Path.HasExtension(fileName) &&
            !File.Exists(fileName))
        {
            var cmdPath = $"{fileName}.cmd";
            // Check common locations
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
            foreach (var dir in pathDirs)
            {
                if (File.Exists(Path.Combine(dir, cmdPath)))
                {
                    fileName = cmdPath;
                    break;
                }
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
            psi.ArgumentList.Add(argument);

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
                psi.Environment[pair.Key] = pair.Value;
        }

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
            // Process was cancelled or killed. Return collected output only.
        }
        catch (ObjectDisposedException)
        {
            // Stream disposed as part of process teardown.
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
            // Process already exited.
        }
    }
}
