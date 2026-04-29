using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

/// <summary>
/// Temporal activity group for Docker container lifecycle: spawn, exec, stream, destroy.
/// First port in the Elsa→Temporal migration; template for remaining activity groups.
/// See temporal.md §7.7 for the full spec.
/// </summary>
public class DockerActivities
{
    private readonly IContainerManager _docker;
    private readonly IGuiPortAllocator? _guiPort;
    private readonly ISessionContainerRegistry? _registry;
    private readonly ISessionContainerLogStreamer? _logStreamer;
    private readonly ISessionStreamSink _sink;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<DockerActivities> _log;

    public DockerActivities(
        IContainerManager docker,
        ISessionStreamSink sink,
        MagicPaiConfig config,
        ILogger<DockerActivities>? log = null,
        IGuiPortAllocator? guiPort = null,
        ISessionContainerRegistry? registry = null,
        ISessionContainerLogStreamer? logStreamer = null)
    {
        _docker = docker;
        _sink = sink;
        _config = config;
        _log = log ?? NullLogger<DockerActivities>.Instance;
        _guiPort = guiPort;
        _registry = registry;
        _logStreamer = logStreamer;
    }

    [Activity]
    public async Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        // Invariant guard: Docker mode is required.
        if (!string.Equals(_config.ExecutionBackend, "docker", StringComparison.OrdinalIgnoreCase))
            throw new ApplicationFailureException(
                "MagicPAI is configured without Docker backend; spawn rejected.",
                errorType: "ConfigError", nonRetryable: true);

        // Pull workflow identity from the activity context so the labels we
        // attach to the container survive a server restart and the GC's
        // fallback sweep can reap orphans (BUG-4). SessionId is required.
        var workflowType = ctx.Info.WorkflowType ?? string.Empty;
        var workflowId = ctx.Info.WorkflowId ?? string.Empty;

        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["magicpai.session"] = input.SessionId,
            ["magicpai.workflow"] = workflowType,
            ["magicpai.workflow_id"] = workflowId,
            // ISO-8601 round-trip so the GC can compare against UtcNow without
            // locale ambiguity. Activity executes outside the workflow body so
            // DateTime.UtcNow is fine here (this is non-deterministic code).
            ["magicpai.created_at"] = DateTime.UtcNow.ToString("O"),
        };

        var config = new ContainerConfig
        {
            Image = input.Image,
            WorkspacePath = input.WorkspacePath,
            MemoryLimitMb = input.MemoryLimitMb,
            EnableGui = input.EnableGui,
            Env = input.EnvVars ?? new Dictionary<string, string>(),
            Labels = labels,
        };

        var ownerId = input.SessionId;
        var allocateGuiPort = input.EnableGui && _guiPort is not null;
        if (allocateGuiPort)
            config.GuiPort = _guiPort!.Reserve(ownerId);

        _log.LogInformation("Spawning container image={Image} workspace={Path}",
            config.Image, config.WorkspacePath);

        try
        {
            var result = await _docker.SpawnAsync(config, ct);
            _registry?.UpdateContainer(ownerId, result.ContainerId, result.GuiUrl);
            _logStreamer?.StartStreaming(ownerId, result.ContainerId);

            await _sink.EmitStructuredAsync(input.SessionId, "ContainerSpawned", new
            {
                containerId = result.ContainerId,
                guiUrl = result.GuiUrl ?? "",
                workspace = config.WorkspacePath
            }, ct);

            return new SpawnContainerOutput(result.ContainerId, result.GuiUrl);
        }
        catch (Exception) when (allocateGuiPort)
        {
            _guiPort!.Release(ownerId);
            throw;  // rethrown as ActivityFailureException by Temporal
        }
    }

    [Activity]
    public async Task<ExecOutput> ExecAsync(ExecInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        try
        {
            // Coerce WorkingDirectory to a container-side Linux path — workflows
            // occasionally pass a host workspace path (e.g. "C:/tmp/foo") which
            // docker-exec rejects with "Cwd must be an absolute path".
            var containerWorkDir = NormalizeContainerWorkDir(input.WorkingDirectory);

            var result = await _docker.ExecAsync(
                input.ContainerId,
                input.Command,
                containerWorkDir,
                ct);

            // Cap output payload to avoid blowing history size.
            var rawOutput = result.Output ?? "";
            var output = rawOutput.Length > 65536
                ? rawOutput[..65536] + "\n...[truncated]..."
                : rawOutput;

            return new ExecOutput(result.ExitCode, output, result.Error);
        }
        catch (OperationCanceledException)
        {
            throw;  // Temporal will mark activity as cancelled
        }
        catch (Exception ex)
        {
            throw new ApplicationFailureException(
                $"Exec in container failed: {ex.Message}",
                errorType: "ExecError", nonRetryable: false);
        }
    }

    [Activity]
    public async Task<StreamOutput> StreamAsync(StreamInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        // Resume-from-heartbeat: if we're retrying, skip lines we already streamed.
        var resumeOffset = 0;
        if (ctx.Info.HeartbeatDetails.Count > 0)
        {
            try
            {
                resumeOffset = await ctx.Info.HeartbeatDetailAtAsync<int>(0);
            }
            catch
            {
                // Fresh start if heartbeat details cannot be decoded.
                resumeOffset = 0;
            }
        }

        var lineCount = 0;
        var lastLine = (string?)null;
        var exitCode = -1;

        try
        {
            // The existing IContainerManager uses callback-based streaming. We adapt
            // it here by pushing emitted chunks through the same line-accounting /
            // heartbeat / SignalR fan-out path the Temporal template calls for.
            var captured = new List<string>();
            void OnOutput(string chunk)
            {
                // Container managers emit chunks that may contain multiple lines.
                foreach (var line in chunk.Split('\n'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    lineCount++;
                    if (lineCount <= resumeOffset) continue;

                    lastLine = line;
                    captured.Add(line);

                    if (input.SessionId is not null)
                    {
                        // Fire-and-forget: SignalR send is non-blocking; any failure is
                        // logged but does not fail the activity (the chunk still counts).
                        try
                        {
                            _sink.EmitChunkAsync(input.SessionId, line, ct).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            _log.LogDebug(ex, "Sink emit failed for {SessionId}", input.SessionId);
                        }
                    }

                    // Heartbeat periodically with our resume marker.
                    if (lineCount % 20 == 0)
                        ctx.Heartbeat(lineCount);
                }
            }

            var result = await _docker.ExecStreamingAsync(
                input.ContainerId,
                input.Command,
                OnOutput,
                TimeSpan.FromMinutes(input.TimeoutMinutes),
                ct);

            exitCode = result.ExitCode;
            return new StreamOutput(exitCode, lineCount, lastLine);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("Stream activity cancelled at line {Line}", lineCount);
            throw;
        }
    }

    [Activity]
    public async Task DestroyAsync(DestroyInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        try
        {
            await _docker.DestroyAsync(input.ContainerId, ct);
            // Best-effort GUI port release (owner id = container id's session).
            _guiPort?.Release(input.ContainerId);
            _registry?.UpdateContainer(input.ContainerId, null);
            if (_logStreamer is not null)
                await _logStreamer.StopStreamingAsync(input.ContainerId);
        }
        catch (Exception ex) when (!input.ForceKill)
        {
            _log.LogWarning(ex, "Soft destroy failed for {Id}, retrying with force", input.ContainerId);
            // IContainerManager's impl uses force on retry.
            await _docker.DestroyAsync(input.ContainerId, ct);
        }
    }

    /// <summary>
    /// Coerces a user-supplied WorkingDirectory to a container-side Linux path.
    /// Non-absolute Linux paths (including Windows drive letters) are replaced
    /// with the configured container workspace mount point.
    /// </summary>
    private string NormalizeContainerWorkDir(string? candidate)
    {
        var containerDefault = _config.ContainerWorkDir ?? "/workspace";
        if (string.IsNullOrWhiteSpace(candidate)) return containerDefault;
        if (candidate.StartsWith('/')) return candidate;
        return containerDefault;
    }
}
