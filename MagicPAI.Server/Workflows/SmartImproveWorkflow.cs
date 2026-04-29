// MagicPAI.Server/Workflows/SmartImproveWorkflow.cs
// Top-level workflow for the burst/verify oscillator described in newplan.md.
//
//   Phase 0  PREPROCESS  (once per session)
//     ContextGatherer       → PROJECT_PROFILE.md
//     GenerateRubric        → DONE_RUBRIC.md
//     PlanVerificationHarness → VERIFICATION_HARNESS.md
//
//   Phase 1  BURST/VERIFY OSCILLATOR
//     burstSchedule = [8, 8, 5, 5, 5, …]   (steady-state 5)
//     repeat:
//       SmartIterativeLoopWorkflow(burstSize)        ← per-burst child
//       VerifyHarnessAsync(run=1, freshSeed)
//       VerifyHarnessAsync(run=2, cleanRebuild=true) ← separated dual-clean
//       ClassifyFailuresAsync (LLM-judge buckets failures)
//       if both runs clean of P0/P1 real failures × 2 → exit "verified-clean"
//       else hard cap checks: max-total / budget / max-bursts
//
// Anti-reward-hacking guards (active throughout):
//   - SmartIterativeLoop layers silence countdown + multi-signal no-progress
//   - Verify is run TWICE per cycle with different seeds + clean rebuild on
//     run #2 → defeats cached/false-greens
//   - Tests/ tripwire surfaces if SmartIterativeLoop reports it during burst
//
// See newplan.md §1 (architecture), §4 (anti-reward-hacking), §5 (burst
// schedule rationale), §7.2 (test plan).
using System.Text.Json;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.SmartImprove;
using MagicPAI.Activities.Stage;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Self-terminating autonomous improvement workflow. Owns the workspace
/// container for the entire run; dispatches FullOrchestrate-style work via
/// SmartIterativeLoop child workflows; gates termination on external
/// verification (build/test/lint/Playwright) — not on model self-report.
/// </summary>
[Workflow]
public class SmartImproveWorkflow
{
    // Observable state (queryable from Studio).
    private string _phase = "initializing";
    private int _totalIterations;
    private int _completedBursts;
    private int _stableVerifyStreak;
    private decimal _totalCost;
    private string? _stopSignalReason;

    // Persisted-across-bursts state.
    private string _projectType = "unknown";
    private string _rubricJson = "{\"items\":[]}";
    private string _harnessScriptPath = "";
    private string _latestFailuresJson = "[]";
    private DoneRubricSnapshot _latestRubricSnapshot =
        new(0, 0, 0, 0, 0, 0);

    [WorkflowQuery] public string Phase => _phase;
    [WorkflowQuery] public int TotalIterations => _totalIterations;
    [WorkflowQuery] public int CompletedBursts => _completedBursts;
    [WorkflowQuery] public int StableVerifyStreak => _stableVerifyStreak;
    [WorkflowQuery] public decimal TotalCostUsd => _totalCost;
    [WorkflowQuery] public string ProjectType => _projectType;

    [WorkflowSignal]
    public Task RequestStopAsync(string reason)
    {
        _stopSignalReason = string.IsNullOrWhiteSpace(reason) ? "signal" : reason;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<SmartImproveOutput> RunAsync(SmartImproveInput input)
    {
        ValidateInput(input);

        // ── Container lifecycle: SmartImprove OWNS the container for the
        // entire multi-burst run. The shared state across iterations
        // (rubric.json, harness.sh, IMPROVEMENTS.md) lives in /workspace
        // and persists naturally because we never destroy the container
        // until the workflow ends.
        _phase = "spawning-container";

        var spawnInput = new SpawnContainerInput(
            SessionId: input.SessionId,
            WorkspacePath: input.WorkspacePath,
            EnableGui: input.EnableGui);

        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(spawnInput),
            ActivityProfiles.Container);

        try
        {
            // ── PHASE 0: PREPROCESS ─────────────────────────────────────
            await RunPreprocessAsync(input, spawn.ContainerId);

            // If the rubric is empty (preprocess generated no items) we
            // can't drive the loop — exit with a clear reason.
            if (_latestRubricSnapshot.TotalItems == 0)
            {
                _phase = "completed-empty-rubric";
                return BuildOutput("no-rubric-items", new List<string>());
            }

            // ── PHASE 1: BURST/VERIFY OSCILLATOR ────────────────────────
            var burstSchedule = ResolveBurstSchedule(input);
            int burstIndex = 0;
            string exitReason = "max-bursts";

            while (true)
            {
                if (_stopSignalReason is not null)
                {
                    exitReason = "cancelled";
                    break;
                }

                if (burstIndex >= input.MaxBursts)
                {
                    exitReason = "max-bursts";
                    break;
                }

                if (_totalIterations >= input.MaxTotalIterations)
                {
                    exitReason = "max-total";
                    break;
                }

                if (input.MaxTotalBudgetUsd > 0m && _totalCost >= input.MaxTotalBudgetUsd)
                {
                    exitReason = "budget";
                    break;
                }

                var burstSize = burstSchedule[Math.Min(burstIndex, burstSchedule.Length - 1)];

                // ── FIX BURST ────────────────────────────────────────────
                _phase = $"burst-{burstIndex + 1}";
                await EmitStageAsync(input.SessionId, "burst-start");
                var burstOut = await RunBurstAsync(input, spawn.ContainerId, burstSize);

                _totalIterations += burstOut.IterationsRun;
                _totalCost += burstOut.TotalCostUsd;
                await EmitCostAsync(input.SessionId, _totalCost);
                // Bump _completedBursts immediately on burst-finish so the
                // observable query reflects the burst we just completed even
                // when the workflow exits before the schedule advances.
                _completedBursts = burstIndex + 1;

                // ── VERIFY (dual-separated runs) ────────────────────────
                _phase = $"verify-{burstIndex + 1}-run-1";
                await EmitStageAsync(input.SessionId, "verifying");
                var run1 = await RunVerifyAsync(input, spawn.ContainerId,
                    cleanRebuild: false, seed: 0);

                _phase = $"verify-{burstIndex + 1}-run-2";
                var run2 = await RunVerifyAsync(input, spawn.ContainerId,
                    cleanRebuild: true, seed: NextSeed(burstIndex));

                // Merge failures from both runs, then classify.
                var mergedFailuresJson = MergeFailureLists(
                    run1.Failures, run2.Failures);

                _phase = $"classify-{burstIndex + 1}";
                await EmitStageAsync(input.SessionId, "repairing");
                var classified = await ClassifyFailuresIfAnyAsync(
                    input, spawn.ContainerId, mergedFailuresJson, run1, run2);

                _latestFailuresJson = classified.ClassifiedFailuresJson;

                // Recompute rubric snapshot from the merged classified result.
                _latestRubricSnapshot = SnapshotFrom(
                    rubricJson: _rubricJson,
                    classifiedFailuresJson: classified.ClassifiedFailuresJson,
                    run1: run1, run2: run2);

                // Termination: clean if BOTH runs reported zero real P0/P1.
                bool bothClean = run1.RealP0Count + run1.RealP1Count == 0
                              && run2.RealP0Count + run2.RealP1Count == 0
                              && classified.RealCount == 0;

                if (bothClean)
                {
                    _stableVerifyStreak++;
                    await EmitStageAsync(input.SessionId, "converged");
                    if (_stableVerifyStreak >= input.RequiredCleanVerifies)
                    {
                        exitReason = "verified-clean";
                        break;
                    }
                }
                else
                {
                    _stableVerifyStreak = 0;
                    await EmitStageAsync(input.SessionId, "escalating");
                }

                burstIndex++;
            }

            _phase = "completed";
            await EmitStageAsync(input.SessionId, "done");

            // Surface remaining P2/P3 items so the caller knows what's left.
            var remaining = ExtractRemainingP2P3(_latestFailuresJson);
            return BuildOutput(exitReason, remaining);
        }
        finally
        {
            _phase = "cleanup";
            var destroyInput = new DestroyInput(spawn.ContainerId);
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(destroyInput),
                ActivityProfiles.ContainerCleanup);
        }
    }

    // ── PHASE 0: Preprocess ─────────────────────────────────────────────

    private async Task RunPreprocessAsync(SmartImproveInput input, string containerId)
    {
        _phase = "preprocess-context";

        // 0a — Gather project profile via existing ContextGatherer child WF.
        var ctxInput = new ContextGathererInput(
            SessionId: input.SessionId,
            Prompt: input.Prompt,
            ContainerId: containerId,
            WorkingDirectory: input.WorkspacePath,
            AiAssistant: input.AiAssistant);

        var context = await Workflow.ExecuteChildWorkflowAsync(
            (ContextGathererWorkflow w) => w.RunAsync(ctxInput),
            new ChildWorkflowOptions { Id = $"{input.SessionId}-context" });

        _totalCost += context.CostUsd;
        await EmitCostAsync(input.SessionId, _totalCost);

        // 0b — Generate the rubric.
        _phase = "preprocess-rubric";

        var rubricInput = new GenerateRubricInput(
            SessionId: input.SessionId,
            ContainerId: containerId,
            WorkspacePath: input.WorkspacePath,
            ProjectProfile: context.GatheredContext,
            OriginalPrompt: input.Prompt,
            AiAssistant: input.AiAssistant,
            ModelPower: 2);

        var rubric = await Workflow.ExecuteActivityAsync(
            (AiActivities a) => a.GenerateRubricAsync(rubricInput),
            ActivityProfiles.Long);

        _projectType = rubric.ProjectType;
        _rubricJson = rubric.RubricJson;
        _totalCost += rubric.CostUsd;
        await EmitCostAsync(input.SessionId, _totalCost);
        _latestRubricSnapshot = new DoneRubricSnapshot(
            TotalItems: rubric.RubricItemCount,
            PassedItems: 0,
            FailedP0: 0, FailedP1: 0, FailedP2: 0, FailedP3: 0);

        // 0c — Plan the verification harness.
        _phase = "preprocess-harness";

        var harnessInput = new PlanVerificationHarnessInput(
            SessionId: input.SessionId,
            ContainerId: containerId,
            WorkspacePath: input.WorkspacePath,
            ProjectType: _projectType,
            RubricJson: _rubricJson,
            AiAssistant: input.AiAssistant,
            ModelPower: 2);

        var harness = await Workflow.ExecuteActivityAsync(
            (AiActivities a) => a.PlanVerificationHarnessAsync(harnessInput),
            ActivityProfiles.Long);

        _harnessScriptPath = harness.HarnessScriptPath;
        _totalCost += harness.CostUsd;
        await EmitCostAsync(input.SessionId, _totalCost);
    }

    // ── PHASE 1 helpers ─────────────────────────────────────────────────

    private async Task<SmartIterativeLoopOutput> RunBurstAsync(
        SmartImproveInput input, string containerId, int burstSize)
    {
        // Each burst's prompt: original user prompt + failures-so-far context.
        // The SmartIterativeLoop uses katz-loop-style RunCliAgent calls
        // internally — no need to re-invoke FullOrchestrate per iteration.
        var burstPrompt = BuildBurstPrompt(input.Prompt, _latestFailuresJson, _completedBursts);

        var loopInput = new SmartIterativeLoopInput(
            SessionId: input.SessionId,
            ContainerId: containerId,
            WorkspacePath: input.WorkspacePath,
            Prompt: burstPrompt,
            AiAssistant: input.AiAssistant,
            Model: null,
            ModelPower: 2,
            MaxIterations: burstSize,
            MinIterations: 1);

        var burstChildId = $"{input.SessionId}-burst-{_completedBursts + 1}";
        return await Workflow.ExecuteChildWorkflowAsync(
            (SmartIterativeLoopWorkflow w) => w.RunAsync(loopInput),
            new ChildWorkflowOptions { Id = burstChildId });
    }

    private async Task<VerifyHarnessOutput> RunVerifyAsync(
        SmartImproveInput input, string containerId, bool cleanRebuild, int seed)
    {
        var verifyInput = new VerifyHarnessInput(
            SessionId: input.SessionId,
            ContainerId: containerId,
            WorkspacePath: input.WorkspacePath,
            HarnessScriptPath: _harnessScriptPath,
            RubricJson: _rubricJson,
            CleanRebuild: cleanRebuild,
            Seed: seed);

        return await Workflow.ExecuteActivityAsync(
            (SmartImproveActivities a) => a.VerifyHarnessAsync(verifyInput),
            ActivityProfiles.Verify);
    }

    private async Task<ClassifyFailuresOutput> ClassifyFailuresIfAnyAsync(
        SmartImproveInput input, string containerId, string failuresJson,
        VerifyHarnessOutput run1, VerifyHarnessOutput run2)
    {
        // Skip the LLM-judge call when nothing failed — no point spending
        // money classifying an empty list.
        var hadFailures = run1.Failures.Count > 0 || run2.Failures.Count > 0;
        if (!hadFailures)
        {
            return new ClassifyFailuresOutput(
                ClassifiedFailuresJson: "[]",
                RealCount: 0, StructuralCount: 0, EnvironmentalCount: 0,
                CostUsd: 0m);
        }

        var classifyInput = new ClassifyFailuresInput(
            SessionId: input.SessionId,
            ContainerId: containerId,
            WorkspacePath: input.WorkspacePath,
            HarnessOutput: SummarizeHarnessForJudge(run1, run2),
            FailuresJson: failuresJson,
            AiAssistant: input.AiAssistant,
            ModelPower: 3);

        var result = await Workflow.ExecuteActivityAsync(
            (AiActivities a) => a.ClassifyFailuresAsync(classifyInput),
            ActivityProfiles.Medium);

        _totalCost += result.CostUsd;
        await EmitCostAsync(input.SessionId, _totalCost);
        return result;
    }

    // ── Pure helpers (deterministic) ─────────────────────────────────────

    /// <summary>
    /// Resolve the burst schedule: caller override OR default
    /// <c>[8, 8, 5, 5, 5, …]</c> with steady state. Capped at
    /// <see cref="SmartImproveInput.MaxBursts"/> entries.
    /// </summary>
    public static int[] ResolveBurstSchedule(SmartImproveInput input)
    {
        if (input.BurstSchedule != null && input.BurstSchedule.Length > 0)
            return input.BurstSchedule;

        // Default: 8, 8, 5, 5, 5, ... (steady-state at SteadyStateBurstSize).
        // Length = MaxBursts so the schedule is fully indexable.
        var sched = new int[Math.Max(1, input.MaxBursts)];
        for (int i = 0; i < sched.Length; i++)
        {
            sched[i] = i switch
            {
                0 => 8,
                1 => 8,
                _ => Math.Max(1, input.SteadyStateBurstSize),
            };
        }
        return sched;
    }

    public static string BuildBurstPrompt(
        string originalPrompt, string failuresJson, int completedBursts)
    {
        if (completedBursts == 0)
        {
            // First burst: pure user prompt + framing.
            return $"""
                {originalPrompt}

                ---
                You are working in a SmartImprove autonomous loop. Verifier
                runs (build/test/lint/etc.) will check your work between
                bursts. Make every iteration produce SHIPPABLE progress.
                """;
        }

        // Subsequent bursts: focus on what the verifier said failed.
        return $"""
            ORIGINAL TASK:
            {originalPrompt}

            ---
            The verifier reported the following failures after the previous
            burst. Fix these PRIORITY-FIRST (P0 → P1 → P2 → P3). Real failures
            are bugs in your code; structural failures mean a test selector
            drifted (update the test); environmental failures are flakes you
            can ignore unless persistent.

            FAILURES (JSON):
            {failuresJson}

            Continue improving. The verifier will re-run after this burst.
            """;
    }

    /// <summary>
    /// Concatenate two verify runs' failure lists, deduping by RubricItemId.
    /// </summary>
    public static string MergeFailureLists(
        IReadOnlyList<RubricFailure> r1,
        IReadOnlyList<RubricFailure> r2)
    {
        var byId = new Dictionary<string, RubricFailure>(StringComparer.Ordinal);
        foreach (var f in r1) byId[f.RubricItemId] = f;
        foreach (var f in r2) byId.TryAdd(f.RubricItemId, f);
        var merged = byId.Values
            .Select(f => new
            {
                rubricItemId = f.RubricItemId,
                priority = f.Priority,
                classification = f.Classification,
                evidence = f.Evidence,
            })
            .ToArray();
        return JsonSerializer.Serialize(merged);
    }

    public static string SummarizeHarnessForJudge(
        VerifyHarnessOutput r1, VerifyHarnessOutput r2)
    {
        // Compact diagnostic summary for ClassifyFailures — full evidence is
        // already inside each RubricFailure. Keep this short to stay under
        // the LLM input cap.
        return $"Run 1: {r1.Failures.Count} failures (P0={r1.RealP0Count} " +
               $"P1={r1.RealP1Count}). Run 2: {r2.Failures.Count} failures " +
               $"(P0={r2.RealP0Count} P1={r2.RealP1Count}).";
    }

    public static DoneRubricSnapshot SnapshotFrom(
        string rubricJson, string classifiedFailuresJson,
        VerifyHarnessOutput run1, VerifyHarnessOutput run2)
    {
        // Total = number of rubric items. Failed = bucketed by priority
        // from classifiedFailuresJson (the post-classification list).
        int total = 0;
        try
        {
            using var doc = JsonDocument.Parse(rubricJson);
            if (doc.RootElement.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
                total = items.GetArrayLength();
        }
        catch (JsonException) { }

        int p0 = 0, p1 = 0, p2 = 0, p3 = 0;
        try
        {
            using var doc = JsonDocument.Parse(classifiedFailuresJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in doc.RootElement.EnumerateArray())
                {
                    var cls = f.TryGetProperty("classification", out var c)
                        ? c.GetString() ?? "real" : "real";
                    if (cls != "real") continue; // structural/environmental don't count
                    var prio = f.TryGetProperty("priority", out var pp)
                        ? pp.GetString() ?? "P1" : "P1";
                    switch (prio)
                    {
                        case "P0": p0++; break;
                        case "P1": p1++; break;
                        case "P2": p2++; break;
                        case "P3": p3++; break;
                    }
                }
            }
        }
        catch (JsonException) { }

        var totalFailed = p0 + p1 + p2 + p3;
        return new DoneRubricSnapshot(
            TotalItems: total,
            PassedItems: Math.Max(0, total - totalFailed),
            FailedP0: p0,
            FailedP1: p1,
            FailedP2: p2,
            FailedP3: p3);
    }

    public static IReadOnlyList<string> ExtractRemainingP2P3(string classifiedFailuresJson)
    {
        var list = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(classifiedFailuresJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in doc.RootElement.EnumerateArray())
                {
                    var cls = f.TryGetProperty("classification", out var c)
                        ? c.GetString() ?? "real" : "real";
                    if (cls != "real") continue;
                    var prio = f.TryGetProperty("priority", out var pp)
                        ? pp.GetString() ?? "" : "";
                    if (prio != "P2" && prio != "P3") continue;
                    var id = f.TryGetProperty("rubricItemId", out var i)
                        ? i.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(id)) list.Add(id);
                }
            }
        }
        catch (JsonException) { }
        return list;
    }

    private SmartImproveOutput BuildOutput(string exitReason, IReadOnlyList<string> remaining) =>
        new(
            ExitReason: exitReason,
            IterationsRun: _totalIterations,
            BurstsCompleted: _completedBursts,
            TotalCostUsd: _totalCost,
            FinalRubric: _latestRubricSnapshot,
            RemainingP2P3Items: remaining);

    /// <summary>
    /// Produce a deterministic-per-burst seed for the second verify run.
    /// Workflow.NewGuid() would also work but is overkill — burstIndex+1
    /// is sufficient and trivially debuggable.
    /// </summary>
    private static int NextSeed(int burstIndex) => 31337 + (burstIndex * 17);

    /// <summary>
    /// Emit a stage transition. Gated on <c>Workflow.Patched("emit-stage-activity-v1")</c>
    /// so old workflow histories — which never scheduled this activity — replay
    /// deterministically.
    /// </summary>
    private static async Task EmitStageAsync(string sessionId, string stage)
    {
        if (!Workflow.Patched("emit-stage-activity-v1")) return;

        var stageInput = new EmitStageInput(sessionId, stage);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitStageAsync(stageInput),
            ActivityProfiles.Short);
    }

    /// <summary>
    /// Broadcast running cost. Gated on <c>Workflow.Patched("emit-cost-activity-v1")</c>
    /// for replay safety.
    /// </summary>
    private static async Task EmitCostAsync(string sessionId, decimal totalCost)
    {
        if (!Workflow.Patched("emit-cost-activity-v1")) return;

        var costInput = new EmitCostInput(sessionId, totalCost);
        await Workflow.ExecuteActivityAsync(
            (StageActivities a) => a.EmitCostAsync(costInput),
            ActivityProfiles.Short);
    }

    private static void ValidateInput(SmartImproveInput input)
    {
        if (string.IsNullOrWhiteSpace(input.SessionId))
            throw new ApplicationFailureException(
                "SessionId is required.",
                errorType: "InvalidInput", nonRetryable: true);
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ApplicationFailureException(
                "Prompt is required.",
                errorType: "InvalidInput", nonRetryable: true);
        if (input.MaxTotalIterations <= 0)
            throw new ApplicationFailureException(
                "MaxTotalIterations must be >= 1",
                errorType: "InvalidInput", nonRetryable: true);
        if (input.MaxBursts <= 0)
            throw new ApplicationFailureException(
                "MaxBursts must be >= 1",
                errorType: "InvalidInput", nonRetryable: true);
        if (input.RequiredCleanVerifies <= 0)
            throw new ApplicationFailureException(
                "RequiredCleanVerifies must be >= 1",
                errorType: "InvalidInput", nonRetryable: true);
    }
}
