# Phase execution guides

Day-by-day step-by-step guides for executing the migration phases described in
`temporal.md` §22.

## Files

| File | Phase | Day | Focus |
|---|---|---|---|
| `Phase1-Day1.md` | 1 | 1 | Infrastructure (Temporal stack + packages) |
| `Phase1-Day2.md` | 1 | 2 | First activity group (DockerActivities) |
| `Phase1-Day3.md` | 1 | 3 | First workflow (SimpleAgentWorkflow) end-to-end |
| `Phase2-Day4.md` | 2 | 4 | Remaining contracts + AI activity group |
| `Phase2-Day5.md` | 2 | 5 | Git/Verify/Blackboard activities |
| `Phase2-Day6.md` | 2 | 6 | Workflow contracts |
| `Phase2-Day7.md` | 2 | 7 | Simple workflows (4 of 15) |
| `Phase2-Day8.md` | 2 | 8 | Core orchestration workflows |
| `Phase2-Day9.md` | 2 | 9 | Complex orchestrators + specialty |
| `Phase2-Day10.md` | 2 | 10 | Server unification |
| `Phase2-Day11.md` | 2 | 11 | Studio rebuild |
| `Phase2-Day12.md` | 2 | 12 | Test cleanup + E2E verification |
| `Phase3-Day13.md` | 3 | 13 | Elsa retirement + DB migration |
| `Phase3-Day14.md` | 3 | 14 | Docs + CI/CD + final sign-off |

## Format

Each daily guide follows the same template:
1. **Objective** — one-line goal.
2. **Duration** — estimated hours.
3. **Prerequisites** — what Day N-1 needed to have accomplished.
4. **Steps** — numbered, with commands.
5. **Definition of done** — checklist.
6. **Troubleshooting** — common issues.
7. **Next** — pointer to next day.
8. **Time spent** — actual record (fill in).

## Usage

Pick the day matching where you are in `SCORECARD.md`. Follow steps in order.
Update SCORECARD.md checkboxes as you complete items.

## AI-assisted execution

If executing with Claude Code, follow prompts in `temporal.md` Appendix HH:
- Day 1: pair programming (human watches each step).
- Days 2-12: semi-autonomous (AI ports; human reviews PR).
- Days 13-14: autonomous with sign-off (AI does; human approves end).

## Feedback

After each day:
- What worked?
- What took longer than estimated?
- What guide improvements needed?

Record in `docs/agent-session-log.md`.

## See also

- `temporal.md` §22 — high-level phase plan.
- `temporal.md` Appendix U — file-level migration order.
- `SCORECARD.md` — live progress tracker.
