using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub container manager for integration tests.
/// Supports configurable handlers and keeps a trace of all exec calls.
/// </summary>
public class StubContainerManager : IContainerManager
{
    private int _spawnCount;
    private readonly Dictionary<string, string?> _guiUrls = new(StringComparer.Ordinal);
    public ExecResult DefaultExecResult { get; set; } = new(0, "ok", "");
    public List<string> SpawnedContainers { get; } = [];
    public List<string> DestroyedContainers { get; } = [];
    public List<StubExecInvocation> ExecInvocations { get; } = [];
    public Func<StubExecInvocation, ExecResult>? ExecHandler { get; set; }
    public Func<StubExecInvocation, StubStreamingPlan>? StreamingHandler { get; set; }
    public Func<string, IReadOnlyList<string>>? ContainerLogProvider { get; set; }

    public void Reset()
    {
        SpawnedContainers.Clear();
        DestroyedContainers.Clear();
        ExecInvocations.Clear();
        _guiUrls.Clear();
        ExecHandler = null;
        StreamingHandler = null;
        ContainerLogProvider = null;
        DefaultExecResult = new ExecResult(0, "ok", "");
    }

    public Task<ContainerInfo> SpawnAsync(ContainerConfig config, CancellationToken ct)
    {
        var id = $"stub-ctr-{Interlocked.Increment(ref _spawnCount):D4}";
        var guiUrl = config.EnableGui ? $"http://localhost:6080/?container={id}" : null;
        SpawnedContainers.Add(id);
        _guiUrls[id] = guiUrl;
        return Task.FromResult(new ContainerInfo(id, guiUrl));
    }

    public Task<ExecResult> ExecAsync(string containerId, string command, string workDir, CancellationToken ct)
    {
        var invocation = new StubExecInvocation(containerId, command, workDir, null, false);
        ExecInvocations.Add(invocation);
        return Task.FromResult(ExecHandler?.Invoke(invocation) ?? DefaultExecResult);
    }

    public Task<ExecResult> ExecAsync(string containerId, ContainerExecRequest request, CancellationToken ct)
    {
        var invocation = new StubExecInvocation(containerId, null, request.WorkingDirectory, request, false);
        ExecInvocations.Add(invocation);
        return Task.FromResult(ExecHandler?.Invoke(invocation) ?? DefaultExecResult);
    }

    public async Task<ExecResult> ExecStreamingAsync(
        string containerId, string command, Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        var invocation = new StubExecInvocation(containerId, command, "", null, true);
        ExecInvocations.Add(invocation);
        var plan = StreamingHandler?.Invoke(invocation);
        return await RunStreamingPlanAsync(plan, onOutput, ct);
    }

    public async Task<ExecResult> ExecStreamingAsync(
        string containerId, ContainerExecRequest request, Action<string> onOutput, TimeSpan timeout, CancellationToken ct)
    {
        var invocation = new StubExecInvocation(containerId, null, request.WorkingDirectory, request, true);
        ExecInvocations.Add(invocation);
        var plan = StreamingHandler?.Invoke(invocation);
        return await RunStreamingPlanAsync(plan, onOutput, ct);
    }

    public Task DestroyAsync(string containerId, CancellationToken ct)
    {
        DestroyedContainers.Add(containerId);
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(string containerId, CancellationToken ct) =>
        Task.FromResult(true);

    public async Task StreamLogsAsync(string containerId, Action<string> onLog, CancellationToken ct)
    {
        foreach (var line in ContainerLogProvider?.Invoke(containerId) ?? [])
        {
            ct.ThrowIfCancellationRequested();
            onLog(line);
            await Task.Yield();
        }
    }

    public string? GetGuiUrl(string containerId) =>
        _guiUrls.TryGetValue(containerId, out var guiUrl) ? guiUrl : null;

    private async Task<ExecResult> RunStreamingPlanAsync(
        StubStreamingPlan? plan,
        Action<string> onOutput,
        CancellationToken ct)
    {
        if (plan is null)
        {
            onOutput(DefaultExecResult.Output);
            return DefaultExecResult;
        }

        foreach (var chunk in plan.Chunks)
        {
            ct.ThrowIfCancellationRequested();
            if (chunk.DelayMs > 0)
                await Task.Delay(chunk.DelayMs, ct);
            onOutput(chunk.Text);
        }

        return plan.Result;
    }
}

public sealed record StubExecInvocation(
    string ContainerId,
    string? Command,
    string WorkingDirectory,
    ContainerExecRequest? Request,
    bool IsStreaming);

public sealed record StubStreamingPlan(
    ExecResult Result,
    IReadOnlyList<StubOutputChunk> Chunks);

public sealed record StubOutputChunk(string Text, int DelayMs = 0);
