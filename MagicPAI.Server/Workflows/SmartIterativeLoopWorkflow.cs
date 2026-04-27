// MagicPAI.Server/Workflows/SmartIterativeLoopWorkflow.cs
// Per-burst child workflow dispatched by SmartImproveWorkflow. Layers six
// research-grounded smart-termination signals on top of the basic
// "iterate-until-done" pattern from IterativeLoopWorkflow:
//
//   1. Silence countdown      — model emits [DONE], 2 extra iters with
//                               EMPTY filesystem delta confirm it.
//   2. Git no-progress         — HEAD unchanged + clean working tree across
//                               consecutive iters (one signal).
//   3. AST-hash no-progress    — lexical normalize-and-hash unchanged across
//                               iters (one signal). Catches whitespace/comment
//                               churn that would defeat git-only detection.
//   4. Tests/ tripwire         — model touched tests/, *.spec.*, *.test.*, or
//                               FooTests.cs during the burst — surfaced to
//                               SmartImprove which routes to verifier scrutiny.
//   5. Question guard          — last response contained "should I?" / "do you
//                               want?" — next prompt forces autonomous mode.
//   6. Min iter / Max iter / Budget — the basic shape from IterativeLoop.
//
// Signal interactions:
//   - Silence countdown ALONE can terminate (advisory [DONE] + 2 empty deltas).
//   - No-progress requires >=NoProgressSignalsRequired (default 2) of the
//     git+AST signals to fire across NoProgressThreshold (default 3) iters.
//   - Tests/ tripwire is a flag — it doesn't terminate the burst, it propagates
//     to the parent so the verifier can scrutinise.
//
// See newplan.md §1 (architecture), §4 (anti-reward-hacking), §7.2 (tests).
using System.Text.RegularExpressions;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.SmartImprove;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Per-burst smart-termination workflow. Receives a container from its
/// parent (no spawn/destroy here — parent owns the container for the entire
/// SmartImprove run). Runs <see cref="SmartIterativeLoopInput.MaxIterations"/>
/// iterations of <c>RunCliAgentAsync</c>, applying multi-signal termination
/// after each iteration.
/// </summary>
[Workflow]
public class SmartIterativeLoopWorkflow
{
    private int _iteration;
    private bool _doneSignalled;
    private bool _silenceConfirmed;
    private bool _testsTripped;
    private decimal _totalCost;
    private int _silenceCount;
    private int _noProgressCount;
    private string? _stopSignalReason;
    private string? _lastResponse;
    private readonly List<string> _modifiedFiles = new();

    // Question detection regex — caches compile once per workflow worker.
    // Workflow.Patched("question-rx") is unnecessary because regex match is
    // deterministic and the workflow body uses it as a pure function.
    private static readonly Regex QuestionRx = new(
        @"\b(should\s+I|which\s+(approach|option|method|way)|do\s+you\s+want|" +
        @"would\s+you\s+(like|prefer)|can\s+you\s+(clarify|confirm)|" +
        @"what\s+(should|do)\s+(I|you)|how\s+(should|do)\s+(I|you)|" +
        @"(is\s+it|is\s+that)\s+(ok|okay|correct|right)\?|please\s+(clarify|confirm|advise))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [WorkflowQuery] public int CurrentIteration => _iteration;
    [WorkflowQuery] public int SilenceCount => _silenceCount;
    [WorkflowQuery] public int NoProgressCount => _noProgressCount;
    [WorkflowQuery] public bool TestsTripped => _testsTripped;
    [WorkflowQuery] public decimal TotalCostUsd => _totalCost;
    [WorkflowQuery] public bool DoneSignalled => _doneSignalled;

    /// <summary>
    /// External "stop gracefully after current iteration" request. The
    /// workflow honors it at the top of the next iteration — it does NOT
    /// cancel an in-flight activity.
    /// </summary>
    [WorkflowSignal]
    public Task RequestStopAsync(string reason)
    {
        _stopSignalReason = string.IsNullOrWhiteSpace(reason) ? "signal" : reason;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<SmartIterativeLoopOutput> RunAsync(SmartIterativeLoopInput input)
    {
        ValidateInput(input);

        // ── Baseline state capture (before any iteration) ───────────────
        // We snapshot now so the FIRST iteration's delta can be computed
        // against the world as it stood when this burst started.
        var (preFsHashes, preAst, preGit) = await CaptureBaselineAsync(input);

        string? assistantSessionId = null;
        string exitReason = "max-iterations";
        bool wasAskingQuestions = false;

        // Carry the previous-iteration state forward.
        var prevFsHashes = preFsHashes;
        var prevAstHash = preAst;
        var prevGitHead = preGit.HeadSha;
        var prevGitClean = preGit.IsClean;
        var anyGit = !preGit.NotAGitRepo;

        while (true)
        {
            // External-stop signal honoured between iterations.
            if (_stopSignalReason is not null)
            {
                exitReason = "signal";
                break;
            }

            // Capture whether [DONE] was already signalled BEFORE this
            // iteration runs. The silence countdown counts iterations AFTER
            // the [DONE] one — per newplan.md ("2 extra loops") and per the
            // user's spec: emitting [DONE] is the claim, not the proof. The
            // proof is N subsequent iterations of zero filesystem delta.
            var wasDoneAtTopOfIter = _doneSignalled;

            _iteration++;

            // Build per-iteration prompt. First iteration carries the user
            // prompt verbatim; subsequent iterations either nudge or — if
            // [DONE] was signalled — explicitly request silence verification.
            var promptToUse = BuildIterationPrompt(input, wasAskingQuestions);

            var runInput = new RunCliAgentInput(
                Prompt: promptToUse,
                ContainerId: input.ContainerId,
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

            // Thread the assistant CLI session id forward so iteration N+1
            // resumes iteration N's conversation (per IterativeLoopWorkflow).
            if (!string.IsNullOrWhiteSpace(run.AssistantSessionId))
                assistantSessionId = run.AssistantSessionId;

            // Track file modifications across the burst.
            foreach (var f in run.FilesModified)
                if (!_modifiedFiles.Contains(f)) _modifiedFiles.Add(f);

            // Budget guard.
            if (input.MaxBudgetUsd > 0m && _totalCost >= input.MaxBudgetUsd)
            {
                exitReason = "budget";
                break;
            }

            // Question-guard input for the NEXT iteration's prompt mutation.
            wasAskingQuestions = input.UseQuestionGuard && QuestionRx.IsMatch(_lastResponse);

            // Done-signal detection. ADVISORY only — does not terminate by
            // itself; just initiates the silence countdown.
            if (ContainsMarker(_lastResponse, input.CompletionMarker))
                _doneSignalled = true;

            // ── Post-iteration state capture ────────────────────────────
            var postFs = await SnapshotIfEnabledAsync(input);
            var postAst = await ComputeAstIfEnabledAsync(input);
            var postGit = await GetGitStateIfEnabledAsync(input);

            // Filesystem delta — drives silence countdown + tests/ tripwire.
            var fsDelta = SmartImproveActivities.ComputeDelta(prevFsHashes, postFs.FileHashes);

            if (input.UseTestsTripwire && fsDelta.TouchedTestFiles)
                _testsTripped = true;

            // Silence countdown — only counts iterations AFTER the [DONE] one.
            // wasDoneAtTopOfIter captures the pre-iteration state, so the
            // iteration where [DONE] is FIRST emitted does not count toward
            // silence (it's the claim, not the proof).
            if (input.UseSilenceCountdown && wasDoneAtTopOfIter)
            {
                if (fsDelta.IsEmpty)
                {
                    _silenceCount++;
                    if (_silenceCount >= input.SilenceCountdownIterations
                        && _iteration >= input.MinIterations)
                    {
                        _silenceConfirmed = true;
                        exitReason = "silence-confirmed";
                        break;
                    }
                }
                else
                {
                    // Filesystem changed during silence — model lied or work
                    // resurfaced. Reset and continue normal loop.
                    _silenceCount = 0;
                }
            }

            // Multi-signal no-progress detection. We have at most TWO signals
            // available within a burst (git, AST) — the third (failure-set)
            // requires running the verifier which only happens BETWEEN bursts.
            //
            // Each signal "fires" if the prev/post comparison shows zero
            // change. We require >=NoProgressSignalsRequired to count this
            // iteration as no-progress.
            int noProgressSignals = 0;
            if (input.UseGitNoProgressGuard && anyGit
                && !string.IsNullOrEmpty(prevGitHead)
                && prevGitHead == postGit.HeadSha
                && prevGitClean && postGit.IsClean)
                noProgressSignals++;

            if (input.UseAstHashGuard
                && !postAst.NoCSharpFiles
                && !string.IsNullOrEmpty(prevAstHash)
                && prevAstHash == postAst.AstHash)
                noProgressSignals++;

            if (noProgressSignals >= input.NoProgressSignalsRequired
                && noProgressSignals > 0)
            {
                _noProgressCount++;
                if (_noProgressCount >= input.NoProgressThreshold
                    && _iteration >= input.MinIterations)
                {
                    exitReason = "no-progress";
                    break;
                }
            }
            else
            {
                _noProgressCount = 0;
            }

            // Roll forward for the next iteration's comparison.
            prevFsHashes = postFs.FileHashes;
            prevAstHash = postAst.AstHash;
            prevGitHead = postGit.HeadSha;
            prevGitClean = postGit.IsClean;
            anyGit = !postGit.NotAGitRepo;

            if (_iteration >= input.MaxIterations)
            {
                // If we were mid-silence-countdown, distinguish exit reason.
                exitReason = _doneSignalled
                    ? "max-iter-during-silence"
                    : "max-iterations";
                break;
            }
        }

        return new SmartIterativeLoopOutput(
            IterationsRun: _iteration,
            ExitReason: exitReason,
            DoneSignalled: _doneSignalled,
            SilenceConfirmed: _silenceConfirmed,
            TestsTripped: _testsTripped,
            TotalCostUsd: _totalCost,
            ModifiedFiles: _modifiedFiles.ToArray(),
            FinalResponse: _lastResponse);
    }

    // ── Helpers (workflow-deterministic) ─────────────────────────────────

    private async Task<(IReadOnlyDictionary<string,string> Fs, string Ast, GetGitStateOutput Git)>
        CaptureBaselineAsync(SmartIterativeLoopInput input)
    {
        var fs = await SnapshotIfEnabledAsync(input);
        var ast = await ComputeAstIfEnabledAsync(input);
        var git = await GetGitStateIfEnabledAsync(input);
        return (fs.FileHashes, ast.AstHash, git);
    }

    private async Task<SnapshotFilesystemOutput> SnapshotIfEnabledAsync(SmartIterativeLoopInput input)
    {
        // Snapshot is needed for both silence countdown AND tests/ tripwire.
        // If neither is enabled we still call but a future optimization could
        // skip it entirely.
        var snapInput = new SnapshotFilesystemInput(
            ContainerId: input.ContainerId,
            WorkspacePath: input.WorkspacePath);

        return await Workflow.ExecuteActivityAsync(
            (SmartImproveActivities a) => a.SnapshotFilesystemAsync(snapInput),
            ActivityProfiles.Medium);
    }

    private async Task<ComputeAstHashOutput> ComputeAstIfEnabledAsync(SmartIterativeLoopInput input)
    {
        if (!input.UseAstHashGuard)
        {
            // Return a deterministic stub — the workflow's no-progress branch
            // checks NoCSharpFiles to know when to skip the AST signal.
            return new ComputeAstHashOutput(
                AstHash: "",
                FilesHashed: 0,
                NoCSharpFiles: true);
        }

        var astInput = new ComputeAstHashInput(
            ContainerId: input.ContainerId,
            WorkspacePath: input.WorkspacePath);

        return await Workflow.ExecuteActivityAsync(
            (SmartImproveActivities a) => a.ComputeAstHashAsync(astInput),
            ActivityProfiles.Medium);
    }

    private async Task<GetGitStateOutput> GetGitStateIfEnabledAsync(SmartIterativeLoopInput input)
    {
        if (!input.UseGitNoProgressGuard)
        {
            return new GetGitStateOutput(
                HeadSha: "",
                DirtyCount: 0,
                IsClean: true,
                NotAGitRepo: true);
        }

        var gitInput = new GetGitStateInput(
            ContainerId: input.ContainerId,
            WorkspacePath: input.WorkspacePath);

        return await Workflow.ExecuteActivityAsync(
            (SmartImproveActivities a) => a.GetGitStateAsync(gitInput),
            ActivityProfiles.Short);
    }

    private string BuildIterationPrompt(SmartIterativeLoopInput input, bool wasAskingQuestions)
    {
        // Iteration 1 — verbatim user prompt + iteration coda.
        if (_iteration == 1)
            return input.Prompt + "\n\n" + DefaultCoda(input.CompletionMarker);

        // [DONE] was signalled last iteration — this iteration is the
        // silence-verification pass.
        if (input.UseSilenceCountdown && _doneSignalled)
        {
            return $"""
                You signalled {input.CompletionMarker} last iteration. This is a silence-verification pass.

                Re-read the original task. Inspect every file you've created or
                modified. If you find ANY remaining work — incomplete logic,
                missing tests, TODO comments, unverified assumptions, untested
                edge cases, broken imports, inconsistent naming, anything — fix
                it now.

                If you genuinely have nothing to do, output a one-line response
                saying so and DO NOT modify any files. The system will detect
                zero filesystem changes and confirm completion.

                {DefaultCoda(input.CompletionMarker)}
                """;
        }

        // Question guard mutation — model asked a question last turn and
        // there's no human to answer. Force autonomous mode.
        if (wasAskingQuestions)
        {
            return $"""
                You asked questions last iteration. This is headless automation
                — no human will answer. Choose the most conservative reasonable
                default and proceed autonomously. Do NOT ask questions in this
                iteration.

                Continue and GO DEEPER on the original task. Add new evidence,
                challenge earlier assumptions, surface more failure modes, or
                refine the implementation.

                {DefaultCoda(input.CompletionMarker)}
                """;
        }

        // Default continuation — push for depth, not surface acknowledgement.
        return $"""
            Continue and GO DEEPER. Add new evidence, challenge earlier
            assumptions, surface more failure modes, or refine the
            implementation. Restate the complete response (every required
            section and every checkbox) — do not reply with a short
            acknowledgement. Only emit the completion marker when you have
            genuinely exhausted additional depth.

            {DefaultCoda(input.CompletionMarker)}
            """;
    }

    private static string DefaultCoda(string marker) =>
        $$"""
        ---
        ## Completion Protocol
        Emit the literal token `{{marker}}` on its own final line ONLY when:
          - all requested work is verifiably complete
          - no TODOs or `// TODO` markers remain that you intended to do
          - your last edit did not break anything you can verify

        Do NOT emit the token to escape the loop. Do NOT lie even if you
        feel stuck. The system will run additional verification iterations
        with a stricter prompt before terminating.
        """;

    public static bool ContainsMarker(string? response, string marker)
    {
        if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(marker))
            return false;
        // Marker must appear on its own line so narrative mentions
        // ("I'll emit [DONE] later") don't false-positive.
        foreach (var line in response.Split('\n'))
            if (line.Trim() == marker) return true;
        return false;
    }

    private static void ValidateInput(SmartIterativeLoopInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerId))
            throw new ApplicationFailureException(
                "ContainerId is required — SmartIterativeLoop expects parent to hand in a container.",
                errorType: "InvalidInput",
                nonRetryable: true);
        if (input.MaxIterations <= 0)
            throw new ApplicationFailureException(
                "MaxIterations must be >= 1",
                errorType: "InvalidInput",
                nonRetryable: true);
        if (input.MinIterations < 0)
            throw new ApplicationFailureException(
                "MinIterations must be >= 0",
                errorType: "InvalidInput",
                nonRetryable: true);
        if (input.MinIterations > input.MaxIterations)
            throw new ApplicationFailureException(
                "MinIterations cannot exceed MaxIterations",
                errorType: "InvalidInput",
                nonRetryable: true);
        if (input.NoProgressThreshold <= 0)
            throw new ApplicationFailureException(
                "NoProgressThreshold must be >= 1",
                errorType: "InvalidInput",
                nonRetryable: true);
        if (input.NoProgressSignalsRequired <= 0)
            throw new ApplicationFailureException(
                "NoProgressSignalsRequired must be >= 1",
                errorType: "InvalidInput",
                nonRetryable: true);
    }
}
