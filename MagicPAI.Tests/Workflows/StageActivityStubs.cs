// MagicPAI.Tests/Workflows/StageActivityStubs.cs
// Shared no-op stubs for the StageActivities methods (EmitStageAsync,
// EmitCostAsync). Workflows now schedule these mid-run gated by
// Workflow.Patched("emit-stage-activity-v1") / "emit-cost-activity-v1", so
// every workflow integration test that uses .AddAllActivities() against a
// stub class also needs these registered. Pulling them out into a shared
// class keeps the per-test stub classes focused on the workflow's domain
// activities and lets new workflows pick the stubs up "for free".
using Temporalio.Activities;
using MagicPAI.Activities.Contracts;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// No-op activity stubs for the StageActivities group. Test stubs that already
/// inherit from a workflow-specific Stubs class can also register an instance
/// of this on their TemporalWorker via a separate
/// <c>.AddAllActivities(new StageActivityStubs())</c> call, OR (preferred for
/// test classes that define their own Stubs class with all activities in one
/// place) copy the two <c>[Activity]</c> methods below into that class.
/// </summary>
public class StageActivityStubs
{
    [Activity] public Task EmitStageAsync(EmitStageInput i) => Task.CompletedTask;
    [Activity] public Task EmitCostAsync(EmitCostInput i) => Task.CompletedTask;
}
