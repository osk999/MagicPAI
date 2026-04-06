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

    public async Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogDebug("Local exec (streaming): {Command}", command);

        var psi = CreateProcessStartInfo(command, ".");
        psi.RedirectStandardInput = false;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var outputBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();

        // Read stdout streaming
        var stdoutTask = Task.Run(async () =>
        {
            var buffer = new char[256];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, cts.Token)) > 0)
            {
                var chunk = new string(buffer, 0, read);
                outputBuilder.Append(chunk);
                onOutput(chunk);
            }
        }, cts.Token);

        // Read stderr
        var stderrTask = Task.Run(async () =>
        {
            stderrBuilder.Append(await process.StandardError.ReadToEndAsync(cts.Token));
        }, cts.Token);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }

        return new ExecResult(
            process.HasExited ? process.ExitCode : -1,
            outputBuilder.ToString(),
            stderrBuilder.ToString());
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
}
