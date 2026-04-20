# Phase 2 — Day 7: Simple workflows (4)

**Objective:** port 4 simple workflows — each ~30-80 lines.

**Duration:** ~5 hours.
**Prerequisites:** Day 6 complete (contracts exist).

---

## Workflows to port

1. `VerifyAndRepairWorkflow` — §H.1
2. `PromptEnhancerWorkflow` — §H.2
3. `ContextGathererWorkflow` — §H.3
4. `PromptGroundingWorkflow` — §H.4

Each follows the pattern:
1. Create new file under `MagicPAI.Server/Workflows/Temporal/` (namespace
   `MagicPAI.Server.Workflows.Temporal`).
2. Copy code from the appendix.
3. Register in `Program.cs` with `.AddWorkflow<T>()`.
4. Write one integration test per workflow.
5. Capture one replay fixture per workflow.
6. Write one replay test per workflow.

---

## Template (per-workflow)

### A. Create workflow file

```csharp
// MagicPAI.Server/Workflows/Temporal/PromptEnhancerWorkflow.cs
using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Contracts;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows.Temporal;

[Workflow]
public class PromptEnhancerWorkflow
{
    [WorkflowRun]
    public async Task<PromptEnhancerOutput> RunAsync(PromptEnhancerInput input)
    {
        // ... body from §H.2 ...
    }
}
```

### B. Register

`Program.cs`:
```csharp
.AddWorkflow<MagicPAI.Server.Workflows.Temporal.PromptEnhancerWorkflow>()
```

### C. Integration test

```csharp
// MagicPAI.Tests/Workflows/PromptEnhancerWorkflowTests.cs
[Collection("Temporal")]
[Trait("Category", "Integration")]
public class PromptEnhancerWorkflowTests(TemporalTestFixture f)
{
    [Fact]
    public async Task EnhancesPrompt_HappyPath()
    {
        var stubs = new StubActivities();
        stubs.EnhancePromptResponder = _ => new EnhancePromptOutput(
            EnhancedPrompt: "enhanced", WasEnhanced: true, Rationale: "ok");

        await using var worker = new TemporalWorker(f.Env.Client,
            new TemporalWorkerOptions("test-pe")
                .AddAllActivities(stubs)
                .AddWorkflow<PromptEnhancerWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var result = await f.Env.Client.ExecuteWorkflowAsync(
                (PromptEnhancerWorkflow wf) => wf.RunAsync(new PromptEnhancerInput(
                    SessionId: "t1", OriginalPrompt: "fix thing",
                    ContainerId: "fake", AiAssistant: "claude", ModelPower: 2)),
                new(id: "t1", taskQueue: "test-pe"));

            result.EnhancedPrompt.Should().Be("enhanced");
        });
    }
}
```

### D. Capture fixture

See Day 3 Step 11 pattern. Save to
`MagicPAI.Tests/Workflows/Histories/prompt-enhancer/happy-path-v1.json`.

### E. Replay test

```csharp
[Theory]
[InlineData("Workflows/Histories/prompt-enhancer/happy-path-v1.json")]
[Trait("Category", "Replay")]
public async Task Replays(string path) { /* ... */ }
```

---

## Execution order

1. Start with `VerifyAndRepairWorkflow` — has a repair loop; needs multiple
   stub responses.
2. Then `PromptEnhancerWorkflow` — simplest.
3. Then `ContextGathererWorkflow`.
4. Finally `PromptGroundingWorkflow` — calls `ContextGathererWorkflow` as child.

Order matters for the last one (depends on 3).

## Build + test after each

Don't move on until each workflow's integration + replay tests pass.

## Commit per workflow

```powershell
git add <files for workflow N>
git commit -m "temporal: port <WorkflowName>"
```

---

## Definition of done

- [ ] 4 workflow classes in `Workflows/Temporal/`.
- [ ] 4 integration tests passing.
- [ ] 4 replay fixtures committed.
- [ ] 4 replay tests passing.
- [ ] All 4 registered in `Program.cs`.
- [ ] SCORECARD updated.

## Next

`Phase2-Day8.md` — core orchestration workflows (SimpleAgent port finalized,
OrchestrateSimplePath, ComplexTaskWorker, OrchestrateComplexPath).
