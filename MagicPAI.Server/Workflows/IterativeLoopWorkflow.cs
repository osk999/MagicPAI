// MagicPAI.Server/Workflows/IterativeLoopWorkflow.cs
// Reusable iteration component. See IterativeLoopContracts.cs for the
// input/output shape and the completion-strategy enum. Designed to be
// dispatched as a child workflow by any orchestrator that needs
// "run until structurally done or we hit max iterations".
using System.Text.RegularExpressions;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Wraps an arbitrary prompt in a bounded iteration loop. Per iteration the
/// workflow (1) calls <see cref="AiActivities.RunCliAgentAsync"/> with the
/// prompt plus a structured-progress coda, (2) checks a completion strategy
/// against the response, and (3) decides whether to continue, stop, or force
/// more iterations to meet the minimum.
/// </summary>
/// <remarks>
/// <para><b>Completion detection strategies</b> (see <see cref="CompletionStrategy"/>):
/// <list type="bullet">
/// <item><description><c>Marker</c> — exact-line match of the marker string.
///       Cheapest; fires any time Claude prints <c>[DONE]</c> on its own line.</description></item>
/// <item><description><c>Classifier</c> — delegates to
///       <see cref="AiActivities.ClassifyAsync"/>. Adds latency/cost but
///       is resilient to prose drift.</description></item>
/// <item><description><c>StructuredProgress</c> (default) — parses the coda's
///       required report (`- [x]` / `- [ ]` checkboxes, `Completion: true`,
///       literal marker). All three signals must agree. Most reliable.</description></item>
/// </list>
/// </para>
/// <para><b>Container lifecycle</b> mirrors <see cref="SimpleAgentWorkflow"/>:
/// callers supplying <see cref="IterativeLoopInput.ExistingContainerId"/> reuse
/// it; otherwise the workflow spawns its own and destroys it in
/// <c>finally</c> using <see cref="ActivityProfiles.ContainerCleanup"/>.
/// </para>
/// <para><b>Loop exit</b>: the workflow stops as soon as
/// <c>(iter &gt;= MinIterations &amp;&amp; isDone) || iter &gt;= MaxIterations
///  || budget exhausted || stop signal received</c>. The final
/// <see cref="IterativeLoopOutput.ExitReason"/> reports which branch fired.
/// </para>
/// </remarks>
[Workflow]
public class IterativeLoopWorkflow
{
    private int _iteration;
    private bool _isDone;
    private decimal _totalCost;
    private string _lastResponse = "";
    private string? _stopSignalReason;
    private IterativeLoopProgress? _lastProgress;

    // Canonical "best" response — snapshot of the iteration that first
    // produced a fully-done result. When MinIterations > 1 forces the loop
    // past its first `[DONE]`, subsequent iterations can degrade (the model
    // often responds with a short "already done" acknowledgement). Without
    // this snapshot the loop would return the degenerate last response.
    private string? _firstDoneResponse;
    private IterativeLoopProgress? _firstDoneProgress;

    [WorkflowQuery] public int CurrentIteration => _iteration;
    [WorkflowQuery] public bool IsDone => _isDone;
    [WorkflowQuery] public decimal TotalCostUsd => _totalCost;
    [WorkflowQuery] public string LastResponse => _lastResponse;
    [WorkflowQuery] public IterativeLoopProgress? LastProgress => _lastProgress;

    /// <summary>
    /// External "stop gracefully after the current iteration" request. The
    /// workflow respects it at the top of the next loop iteration — it does
    /// NOT cancel an in-flight activity.
    /// </summary>
    [WorkflowSignal]
    public Task RequestStopAsync(string reason)
    {
        _stopSignalReason = string.IsNullOrWhiteSpace(reason) ? "signal" : reason;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<IterativeLoopOutput> RunAsync(IterativeLoopInput input)
    {
        ValidateInput(input);

        // --- Container-lifecycle branching (same pattern as SimpleAgent / Fix #2)
        string containerId;
        bool ownsContainer;
        if (!string.IsNullOrWhiteSpace(input.ExistingContainerId))
        {
            containerId = input.ExistingContainerId;
            ownsContainer = false;
        }
        else
        {
            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkspacePath,
                EnableGui: input.EnableGui);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        string? assistantSessionId = null;
        string exitReason = "max-iterations";
        var coda = input.CodaOverride ?? BuildDefaultCoda(input.CompletionMarker);

        try
        {
            while (true)
            {
                // Between-iteration signal check. Activities in flight are
                // never interrupted — we wait until they complete.
                if (_stopSignalReason is not null)
                {
                    exitReason = "signal";
                    break;
                }

                _iteration++;

                // First iteration carries the user prompt verbatim; subsequent
                // iterations rely on Claude's --resume (AssistantSessionId)
                // for conversational context and just nudge it to continue.
                // Continuation prompt pushes for DEPTH rather than just asking
                // "are you done". Without this nudge the model often replies
                // "already done" on iterations forced by MinIterations and the
                // overall output degrades on the later pass.
                var prompt = _iteration == 1
                    ? input.Prompt + "\n\n" + coda
                    : "Continue and GO DEEPER. Add new evidence, challenge earlier assumptions, "
                      + "surface more failure modes, or refine the plan. Restate the complete "
                      + "response (every required section and every checkbox) — do not reply with "
                      + "a short acknowledgement. Only emit the completion marker when you have "
                      + "genuinely exhausted additional depth.\n\n" + coda;

                var runInput = new RunCliAgentInput(
                    Prompt: prompt,
                    ContainerId: containerId,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model,
                    ModelPower: input.ModelPower,
                    WorkingDirectory: input.WorkspacePath,
                    SessionId: input.SessionId,
                    AssistantSessionId: assistantSessionId);

                var run = await Workflow.ExecuteActivityAsync(
                    (AiActivities a) => a.RunCliAgentAsync(runInput),
                    ActivityProfiles.Long);

                _lastResponse = run.Response ?? "";
                _totalCost += run.CostUsd;
                // Thread the Claude session id forward so iteration N+1 resumes
                // iteration N's conversation instead of starting over.
                if (!string.IsNullOrWhiteSpace(run.AssistantSessionId))
                    assistantSessionId = run.AssistantSessionId;

                // Budget guard
                if (input.MaxBudgetUsd > 0m && _totalCost >= input.MaxBudgetUsd)
                {
                    exitReason = "budget";
                    break;
                }

                // Completion detection
                (_isDone, _lastProgress) =
                    await DetectCompletionAsync(input, _lastResponse, containerId);

                // Snapshot the FIRST iteration whose response is fully done.
                // The last response may be near-empty if MinIterations forces
                // the loop past that point — prefer the canonical report over
                // a "nothing left to do" acknowledgement.
                if (_isDone && _firstDoneResponse is null)
                {
                    _firstDoneResponse = _lastResponse;
                    _firstDoneProgress = _lastProgress;
                }

                if (_isDone && _iteration >= input.MinIterations)
                {
                    exitReason = "done";
                    break;
                }

                if (_iteration >= input.MaxIterations)
                {
                    exitReason = "max-iterations";
                    break;
                }
            }

            // Prefer the first-done snapshot when the loop was forced past it
            // by MinIterations. Fall back to the last response when the loop
            // never got to done (max-iterations, budget, signal).
            var canonicalResponse = _firstDoneResponse ?? _lastResponse;
            var canonicalProgress = _firstDoneProgress ?? _lastProgress;

            return new IterativeLoopOutput(
                FinalResponse: canonicalResponse,
                DoneMarkerObserved: _isDone,
                IterationsRun: _iteration,
                TotalCostUsd: _totalCost,
                ExitReason: exitReason,
                FinalProgress: canonicalProgress);
        }
        finally
        {
            if (ownsContainer)
            {
                var destroyInput = new DestroyInput(containerId);
                await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.DestroyAsync(destroyInput),
                    ActivityProfiles.ContainerCleanup);
            }
        }
    }

    private static void ValidateInput(IterativeLoopInput input)
    {
        // Validation failures are definitional — raise a non-retryable
        // ApplicationFailure so Temporal fails the workflow immediately
        // instead of retrying the workflow task forever.
        if (input.MaxIterations <= 0)
            throw new ApplicationFailureException(
                "MaxIterations must be >= 1",
                "InvalidInput",
                nonRetryable: true);
        if (input.MinIterations < 0)
            throw new ApplicationFailureException(
                "MinIterations must be >= 0",
                "InvalidInput",
                nonRetryable: true);
        if (input.MinIterations > input.MaxIterations)
            throw new ApplicationFailureException(
                "MinIterations cannot exceed MaxIterations",
                "InvalidInput",
                nonRetryable: true);
    }

    private async Task<(bool done, IterativeLoopProgress? progress)>
        DetectCompletionAsync(IterativeLoopInput input, string response, string containerId)
    {
        switch (input.CompletionStrategy)
        {
            case CompletionStrategy.Marker:
                return (ContainsMarker(response, input.CompletionMarker), null);

            case CompletionStrategy.StructuredProgress:
                {
                    var progress = ParseProgress(response, input.CompletionMarker);
                    // All signals must agree: no open tasks, completion flag
                    // explicitly true, marker present on its own line, AND
                    // the task checklist meets the caller's minimum size
                    // (defaults to 1 — Math.Max prevents the model gaming the
                    // protocol with a single generic "done" item).
                    var minTasks = Math.Max(1, input.MinRequiredTasks);
                    var done = progress.OpenTaskDescriptions.Count == 0
                               && progress.CompletionFlag
                               && progress.MarkerPresent
                               && progress.TotalTasks >= minTasks;
                    return (done, progress);
                }

            case CompletionStrategy.Classifier:
                {
                    var classifyInput = new ClassifierInput(
                        Prompt: response,
                        ClassificationQuestion:
                            input.CompletionInstructions
                            ?? "Based on this response, has the user's full task been completed successfully? Return true if fully done, false otherwise.",
                        ContainerId: containerId,
                        ModelPower: 3,
                        AiAssistant: input.AiAssistant,
                        SessionId: input.SessionId);

                    var result = await Workflow.ExecuteActivityAsync(
                        (AiActivities a) => a.ClassifyAsync(classifyInput),
                        ActivityProfiles.Medium);

                    return (result.Result, null);
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown CompletionStrategy: {input.CompletionStrategy}");
        }
    }

    private static bool ContainsMarker(string? response, string marker)
    {
        if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(marker))
            return false;
        // Require the marker on its own line so narrative mentions
        // ("I will emit [DONE] later") don't false-positive.
        foreach (var line in response.Split('\n'))
            if (line.Trim() == marker) return true;
        return false;
    }

    // --- Structured-progress parser ---------------------------------------

    private static readonly Regex OpenTaskRx =
        new(@"^\s*-\s*\[\s\]\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ClosedTaskRx =
        new(@"^\s*-\s*\[x\]\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex CompletionFlagRx =
        new(@"^\s*Completion\s*:\s*(true|false)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static IterativeLoopProgress ParseProgress(string response, string marker)
    {
        var open = OpenTaskRx.Matches(response)
            .Select(m => m.Groups[1].Value.Trim())
            .ToArray();
        var closedCount = ClosedTaskRx.Matches(response).Count;

        bool completionFlag = false;
        var flagMatch = CompletionFlagRx.Match(response);
        if (flagMatch.Success)
            completionFlag = string.Equals(flagMatch.Groups[1].Value, "true",
                StringComparison.OrdinalIgnoreCase);

        return new IterativeLoopProgress(
            TotalTasks: open.Length + closedCount,
            CompletedTasks: closedCount,
            CompletionFlag: completionFlag,
            MarkerPresent: ContainsMarker(response, marker),
            OpenTaskDescriptions: open);
    }

    // --- Default coda -----------------------------------------------------

    internal static string BuildDefaultCoda(string marker) =>
        $$"""
        ---
        ## Iteration Protocol (REQUIRED)

        Before ending each turn, emit a progress report in this exact format:

        ### Task Status
        - [ ] <task description> — <one-line status or why still open>
        - [x] <task description> — <one-line summary of what was completed>
        (List every distinct task. Check them off as they are truly done.)

        ### Current Work
        <what you worked on this turn>

        ### Blockers
        <any blockers, or "None">

        ### Completion
        Completion: <true|false>

        ---

        Emit this EXACT token on its own final line, and ONLY when:
        - every task is `- [x]`
        - `Completion: true`
        - no blockers remain

        Token:
        {{marker}}

        Do NOT emit the token while any task is `- [ ]` or while
        `Completion: false`. Do NOT emit it for narrative reasons.
        """;
}
