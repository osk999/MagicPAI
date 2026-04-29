using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Stage;

/// <summary>
/// Temporal activity group for pipeline-stage transitions. Workflows call
/// <see cref="EmitStageAsync"/> at each phase boundary (e.g. "architect",
/// "workers", "coverage-repair", "done") so the session stream sink can push
/// the new stage to connected SignalR clients and Studio updates the stage
/// chip in real time.
/// </summary>
/// <remarks>
/// <para>
/// Workflow code is deterministic and cannot perform I/O directly. The
/// previous implementation only mutated a private <c>_pipelineStage</c> field
/// readable via <c>[WorkflowQuery]</c>, but the Studio UI does not poll
/// queries — so the chip stayed at the field default for the entire session.
/// This activity is the side-effect path: workflows orchestrate, this
/// activity emits.
/// </para>
/// <para>
/// Sink failures are swallowed and logged at debug level. Stage emission is a
/// UX concern; a missing stage update must never fail a workflow.
/// </para>
/// </remarks>
public class StageActivities
{
    private readonly ISessionStreamSink _sink;
    private readonly ILogger<StageActivities> _log;

    public StageActivities(
        ISessionStreamSink sink,
        ILogger<StageActivities>? log = null)
    {
        _sink = sink;
        _log = log ?? NullLogger<StageActivities>.Instance;
    }

    /// <summary>
    /// Emit a pipeline-stage transition for the given session through the
    /// session stream sink. Used by workflows at every phase boundary so the
    /// Studio stage chip reflects the live workflow state.
    /// </summary>
    [Activity]
    public async Task EmitStageAsync(EmitStageInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        try
        {
            await _sink.EmitStageAsync(input.SessionId, input.Stage, ct);
        }
        catch (Exception ex)
        {
            // Stage emission is UX-only — never fail a workflow over a sink hiccup.
            _log.LogDebug(ex,
                "Sink EmitStageAsync failed for session {SessionId} stage {Stage}",
                input.SessionId, input.Stage);
        }
    }

    /// <summary>
    /// Emit a running total-cost-USD update for the given session. Workflows call
    /// this immediately after each <c>_totalCost +=</c> mutation so the Studio
    /// cost tile updates live mid-run instead of only at completion (which the
    /// <c>WorkflowCompletionMonitor</c> still queries via <c>TotalCostUsd</c>).
    /// </summary>
    /// <remarks>
    /// Sink failures are swallowed and logged at debug. Cost emission is UX-only;
    /// a missing tile update must never fail a workflow.
    /// </remarks>
    [Activity]
    public async Task EmitCostAsync(EmitCostInput input)
    {
        var ctx = ActivityExecutionContext.Current;
        var ct = ctx.CancellationToken;

        try
        {
            await _sink.EmitCostAsync(input.SessionId, input.TotalCostUsd, ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex,
                "Sink EmitCostAsync failed for session {SessionId} cost {Cost}",
                input.SessionId, input.TotalCostUsd);
        }
    }
}
