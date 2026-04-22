// MagicPAI.Server/Workflows/Temporal/ResearchPipelineWorkflow.cs
// Temporal port of the Elsa ResearchPipelineWorkflow — now iterative.
// Instead of firing a single ResearchPromptAsync activity, this workflow
// wraps the research prompt in IterativeLoopWorkflow so Claude can
// research → reflect → research deeper until the rewrite is grounded and
// the structured-progress coda emits [DONE] (or MaxIterations / budget
// exhausts). See temporal.md §H.8.
using System.Text.RegularExpressions;
using Temporalio.Workflows;
using MagicPAI.Activities.Contracts;
using MagicPAI.Activities.Docker;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Delegates research to <see cref="IterativeLoopWorkflow"/> so Claude can
/// iterate on codebase understanding until its rewrite is grounded. The
/// outer structure of the response is still the four H2 sections
/// (<c>## Rewritten Task</c>, <c>## Codebase Analysis</c>,
/// <c>## Research Context</c>, <c>## Rationale</c>); the inner loop coda
/// uses H3 sections so parsing the H2s remains unambiguous.
/// </summary>
/// <remarks>
/// <para>Container-lifecycle branching mirrors <see cref="SimpleAgentWorkflow"/>
/// (Fix #2). When dispatched top-level via HTTP, <c>ContainerId</c> is empty
/// and the workflow spawns its own container (destroyed in <c>finally</c>).
/// When nested (e.g. by <see cref="DeepResearchOrchestrateWorkflow"/>), it
/// reuses the caller's container by passing it through as
/// <see cref="IterativeLoopInput.ExistingContainerId"/>.</para>
/// <para>Model power is pinned to 1 (strongest) — deep research is the whole
/// point of this workflow; cost is capped per call via
/// <see cref="IterativeLoopInput.MaxBudgetUsd"/>.</para>
/// </remarks>
[Workflow]
public class ResearchPipelineWorkflow
{
    [WorkflowRun]
    public async Task<ResearchPipelineOutput> RunAsync(ResearchPipelineInput input)
    {
        string containerId;
        bool ownsContainer;
        if (!string.IsNullOrWhiteSpace(input.ContainerId))
        {
            containerId = input.ContainerId;
            ownsContainer = false;
        }
        else
        {
            var spawnInput = new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkingDirectory,
                EnableGui: false);

            var spawn = await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.SpawnAsync(spawnInput),
                ActivityProfiles.Container);

            containerId = spawn.ContainerId;
            ownsContainer = true;
        }

        try
        {
            var researchPrompt = BuildResearchPrompt(input.Prompt);

            var loopInput = new IterativeLoopInput(
                SessionId: input.SessionId,
                Prompt: researchPrompt,
                AiAssistant: input.AiAssistant,
                Model: null,
                ModelPower: 1,
                // Force at least 3 deep passes — a single pass lets Claude
                // emit a shallow generic report on the first try. Each later
                // pass MUST add new depth (different angle, external refs,
                // failure-mode analysis, rejected alternatives).
                MinIterations: 3,
                MaxIterations: 20,
                CompletionStrategy: CompletionStrategy.StructuredProgress,
                WorkspacePath: input.WorkingDirectory,
                ExistingContainerId: containerId,
                EnableGui: false,
                // The research coda defines 12 explicit tasks (A1..A3, B1..B3,
                // C1..C4, D1..D2). Require them all before accepting done so
                // the model can't short-circuit with a trivial "done" report.
                MinRequiredTasks: 12,
                MaxBudgetUsd: 4.0m);

            var loop = await Workflow.ExecuteChildWorkflowAsync(
                (IterativeLoopWorkflow w) => w.RunAsync(loopInput),
                new ChildWorkflowOptions { Id = $"{input.SessionId}-research-loop" });

            // Prefer to read the canonical report from the on-disk
            // `/workspace/research.md` file — the model sometimes emits the
            // full report on one iteration but a short "already done" coda on
            // the next, in which case the loop's FinalResponse no longer
            // carries the H2 sections. The file does. Fall back to parsing
            // the FinalResponse when the file is unreadable / missing.
            var rewritten = ExtractSection(loop.FinalResponse, "Rewritten Task");
            var context = ExtractSection(loop.FinalResponse, "Research Context")
                          ?? ExtractSection(loop.FinalResponse, "Codebase Analysis");

            if (string.IsNullOrWhiteSpace(rewritten))
            {
                var readInput = new ExecInput(
                    ContainerId: containerId,
                    Command: "cat /workspace/research.md",
                    WorkingDirectory: input.WorkingDirectory,
                    TimeoutSeconds: 30);

                var catResult = await Workflow.ExecuteActivityAsync(
                    (DockerActivities a) => a.ExecAsync(readInput),
                    ActivityProfiles.Short);

                if (catResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(catResult.Output))
                {
                    rewritten = ExtractSection(catResult.Output, "Rewritten Task") ?? rewritten;
                    context = ExtractSection(catResult.Output, "Research Context")
                              ?? ExtractSection(catResult.Output, "Codebase Analysis")
                              ?? context;
                }
            }

            return new ResearchPipelineOutput(
                ResearchedPrompt: string.IsNullOrWhiteSpace(rewritten) ? "" : rewritten,
                ResearchContext: context ?? "",
                CostUsd: loop.TotalCostUsd);
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

    // H2 only (`## Heading`) — keeps us from colliding with the loop coda's H3
    // Task Status / Current Work / Completion blocks.
    private static readonly Regex SectionRx = new(
        @"^##\s+(?<name>[^\r\n]+?)\s*$(?<body>[\s\S]*?)(?=^##\s|\z)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static string? ExtractSection(string response, string heading)
    {
        if (string.IsNullOrEmpty(response)) return null;
        foreach (Match m in SectionRx.Matches(response))
        {
            if (string.Equals(m.Groups["name"].Value.Trim(), heading,
                    StringComparison.OrdinalIgnoreCase))
            {
                return m.Groups["body"].Value.Trim();
            }
        }
        return null;
    }

    internal static string BuildResearchPrompt(string originalTask) => $$"""
        You are conducting MULTI-PASS deep research for the task below.
        The host workflow will run you at least THREE times. Each pass
        must add genuinely new depth — a single shallow overview is NOT
        acceptable. Do not emit the completion marker on the first pass.

        Task:
        {{originalTask}}

        ────────────────────────────────────────────────────────────────
        Required per-pass focus (complete ALL of these before you may
        emit the completion marker — spread the work across iterations):

        Pass A — Problem framing
        - Survey every file in the workspace (`ls -la`, read anything that
          could be relevant).
        - Nail down the concrete scope: what must be built, what is out of
          scope, what quality bar applies, who uses it.
        - Enumerate the open questions a thoughtful implementer would ask.

        Pass B — Options & trade-offs
        - Propose AT LEAST 3 candidate technical approaches / stacks /
          architectures.
        - For each, list: strengths, weaknesses, concrete tradeoffs, and
          when it's the wrong choice.
        - Recommend ONE, with a "why not the others" paragraph that treats
          the rejected options seriously.
        - List at least 5 external references (docs, papers, well-known
          posts, repos) relevant to the recommended approach, with URLs.

        Pass C — Plan, risks, verification
        - Break the chosen approach into a concrete implementation plan
          with at least 6 ordered milestones.
        - For each milestone, call out the biggest failure mode and how
          you'd detect it.
        - Propose a verification / testing strategy (unit, integration,
          smoke, manual). Name specific libraries or tools.
        - List at least 5 "what could go wrong" items — performance,
          security, concurrency, scaling, UX. For each, say how you'd
          measure and mitigate.

        ────────────────────────────────────────────────────────────────
        Structure the FINAL response (the last iteration) as these four
        H2 sections in this exact order (use `## ` — exactly two hashes):

        ## Rewritten Task
        (a detailed, grounded rewrite; carry forward the framing from
        Pass A plus the concrete recommendations from Pass B/C)

        ## Codebase Analysis
        (files, patterns, conventions — explicitly call out when the
        workspace is empty / greenfield)

        ## Research Context
        (external docs, references, and the 3-way stack comparison from
        Pass B with URLs; include the rejected options and why)

        ## Rationale
        (why the rewrite is complete, what's still uncertain, the
        milestone plan + failure modes + verification strategy from Pass
        C, and the full list of files/resources inspected)

        After you have produced the four sections above, write the ENTIRE
        research report (exactly those four H2 sections, same order and
        wording) to `/workspace/research.md`. Overwrite if it exists. No
        front-matter, no code fences — the file must begin with the
        literal line `## Rewritten Task`. Verify by reading the file back.

        ────────────────────────────────────────────────────────────────
        Track these tasks in the iteration protocol's checkbox list (use
        `### Task Status` — three hashes — as the coda specifies):

        - [A1] Survey every file in the workspace
        - [A2] Define concrete scope + out-of-scope list
        - [A3] Enumerate the top open questions
        - [B1] Propose ≥3 candidate approaches with trade-offs
        - [B2] Recommend one + explain why not the others
        - [B3] Cite ≥5 external references with URLs
        - [C1] Produce ≥6-milestone implementation plan
        - [C2] Per-milestone failure modes + detection
        - [C3] Verification / testing strategy with specific tools
        - [C4] ≥5 "what could go wrong" risks + mitigations
        - [D1] Produce the four grounded H2 sections
        - [D2] Write `/workspace/research.md` and verify by read-back

        Rules:
        1. On the FIRST pass, most tasks must still be `- [ ]`. Never emit
           the completion marker on pass 1. Use the pass to dig in.
        2. On each continuation pass, CLOSE more tasks and ADD DEPTH —
           new references, stronger rejected-options analysis, sharper
           failure modes. Never simply re-state the previous pass.
        3. Only emit the completion marker when every task above is
           `- [x]` AND `Completion: true` AND the four H2 sections are
           in the response AND `/workspace/research.md` has been written
           and read back successfully.
        """;
}
