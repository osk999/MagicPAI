using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub container manager for integration tests.
/// Returns fake container IDs and configurable exec responses.
/// </summary>
public class StubContainerManager : IContainerManager
{
    private int _spawnCount;
    public ExecResult DefaultExecResult { get; set; } = new(0, "ok", "");
    public List<string> SpawnedContainers { get; } = [];
    public List<string> DestroyedContainers { get; } = [];

    public Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
    {
        var id = $"stub-ctr-{Interlocked.Increment(ref _spawnCount):D4}";
        SpawnedContainers.Add(id);
        return Task.FromResult(new ContainerInfo(id, null));
    }

    public Task<ExecResult> ExecAsync(string containerId, string command, string workDir, CancellationToken ct)
    {
        return Task.FromResult(DefaultExecResult);
    }

    public Task<ExecResult> ExecAsync(string containerId, ContainerExecRequest request, CancellationToken ct)
    {
        return Task.FromResult(DefaultExecResult);
    }

    public Task<ExecResult> ExecStreamingAsync(
        string containerId, string command, Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        onOutput(DefaultExecResult.Output);
        return Task.FromResult(DefaultExecResult);
    }

    public Task<ExecResult> ExecStreamingAsync(
        string containerId, ContainerExecRequest request, Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        onOutput(DefaultExecResult.Output);
        return Task.FromResult(DefaultExecResult);
    }

    public Task DestroyAsync(string containerId, CancellationToken ct)
    {
        DestroyedContainers.Add(containerId);
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(string containerId, CancellationToken ct) =>
        Task.FromResult(true);

    public string? GetGuiUrl(string containerId) => null;
}
