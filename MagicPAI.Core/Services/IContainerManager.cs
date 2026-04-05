using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public interface IContainerManager
{
    /// <summary>Create and start a worker container.</summary>
    Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct);

    /// <summary>Execute a command in a running container. Returns exit code + output.</summary>
    Task<ExecResult> ExecAsync(string containerId, string command,
        string workDir, CancellationToken ct);

    /// <summary>Execute with real-time output streaming.</summary>
    Task<ExecResult> ExecStreamingAsync(string containerId, string command,
        Action<string> onOutput, TimeSpan timeout, CancellationToken ct);

    /// <summary>Stop and remove a container.</summary>
    Task DestroyAsync(string containerId, CancellationToken ct);

    /// <summary>Check if container is running.</summary>
    Task<bool> IsRunningAsync(string containerId, CancellationToken ct);

    /// <summary>Get container GUI URL (if noVNC enabled).</summary>
    string? GetGuiUrl(string containerId);
}
