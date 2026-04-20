using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Verification;

/// <summary>
/// Temporal activity group for post-execution verification and repair-prompt
/// generation. Wraps the existing <see cref="VerificationPipeline"/> as a
/// Temporal activity and emits a <c>VerificationComplete</c> structured event
/// for UI badges / notifications. See temporal.md §I.3 and §7.5.
/// </summary>
/// <remarks>
/// <para>
/// The Elsa activities that previously wrapped the pipeline (<c>RunVerificationActivity</c>
/// and <c>RepairActivity</c>) are still in the tree until Phase 3; this class mirrors
/// their behavior through the Temporal SDK so the Temporal workflows can take over
/// without touching the pipeline itself.
/// </para>
/// <para>
/// Note: the plan template in temporal.md §I.3 assumes
/// <c>VerificationPipeline.RunAsync</c> has a shape like
/// <c>(containerId, workDir, gates, workerOutput, ct)</c>. The real MagicPAI
/// pipeline's shape is
/// <c>(IContainerManager, containerId, workDir, string[] gateFilter, string? workerOutput, ct)</c>.
/// We adapt to the real signature here — same outcome, same gate filtering.
/// </para>
/// </remarks>
public class VerifyActivities
{
    private readonly IContainerManager _docker;
    private readonly VerificationPipeline _pipeline;
    private readonly MagicPaiConfig _config;
    private readonly ISessionStreamSink _sink;
    private readonly ILogger<VerifyActivities> _log;

    public VerifyActivities(
        IContainerManager docker,
        VerificationPipeline pipeline,
        MagicPaiConfig config,
        ISessionStreamSink sink,
        ILogger<VerifyActivities>? log = null)
    {
        _docker = docker;
        _pipeline = pipeline;
        _config = config;
        _sink = sink;
        _log = log ?? NullLogger<VerifyActivities>.Instance;
    }

    /// <summary>
    /// Run the configured verification gates against the worker's output and the
    /// repo state in the container. Emits <c>VerificationComplete</c> through the
    /// session stream sink so Studio can update verification badges.
    /// </summary>
    [Activity]
    public async Task<VerifyOutput> RunGatesAsync(VerifyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "RunGates requires a ContainerId; verification gates run inside a worker container.",
                errorType: "ConfigError", nonRetryable: true);

        var ct = ActivityExecutionContext.Current.CancellationToken;

        var gateFilter = input.EnabledGates?.ToArray() ?? Array.Empty<string>();

        // WorkingDirectory must be a container-side Linux absolute path; workflows
        // sometimes pass a host WorkspacePath (e.g. "C:/tmp/foo" on Windows) that
        // Docker exec rejects with "Cwd must be an absolute path". Coerce to the
        // container's configured workspace mount when the value isn't a Linux path.
        var containerWorkDir = NormalizeContainerWorkDir(input.WorkingDirectory);

        var pipelineResult = await _pipeline.RunAsync(
            _docker,
            input.ContainerId,
            containerWorkDir,
            gateFilter,
            input.WorkerOutput,
            ct);

        var failed = pipelineResult.Gates
            .Where(r => !r.Passed)
            .Select(r => r.Name)
            .ToList();

        var resultsJson = JsonSerializer.Serialize(pipelineResult.Gates);

        if (input.SessionId is not null)
        {
            try
            {
                await _sink.EmitStructuredAsync(
                    input.SessionId, "VerificationComplete", pipelineResult.Gates, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Sink emit failed for {SessionId}", input.SessionId);
            }
        }

        return new VerifyOutput(
            AllPassed: pipelineResult.AllPassed,
            FailedGates: failed,
            GateResultsJson: resultsJson);
    }

    /// <summary>
    /// Pure CPU — generate a repair prompt from failed gates and the original
    /// request. Returns <see cref="RepairOutput.ShouldAttemptRepair"/> = <c>false</c>
    /// when the caller has exhausted its repair budget.
    /// </summary>
    [Activity]
    public Task<RepairOutput> GenerateRepairPromptAsync(RepairInput input)
    {
        if (input.AttemptNumber > input.MaxAttempts)
            return Task.FromResult(new RepairOutput(RepairPrompt: "", ShouldAttemptRepair: false));

        var prompt = $"""
            Fix the following failed verification gates. Be concise and surgical.

            Failed gates: {string.Join(", ", input.FailedGates ?? Array.Empty<string>())}
            Original request: {input.OriginalPrompt}
            Gate details:
            {input.GateResultsJson}

            Attempt {input.AttemptNumber} of {input.MaxAttempts}.
            """;

        return Task.FromResult(new RepairOutput(RepairPrompt: prompt, ShouldAttemptRepair: true));
    }

    /// <summary>
    /// Coerces a user-supplied WorkingDirectory to a container-side Linux path.
    /// Values that aren't Linux absolute paths are replaced with the configured
    /// container workspace mount.
    /// </summary>
    private string NormalizeContainerWorkDir(string? candidate)
    {
        var containerDefault = _config.ContainerWorkDir ?? "/workspace";
        if (string.IsNullOrWhiteSpace(candidate)) return containerDefault;
        if (candidate.StartsWith('/')) return candidate;
        return containerDefault;
    }
}
