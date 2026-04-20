# Phase 1 — Day 3: First workflow end-to-end (SimpleAgentWorkflow)

**Objective:** port `AiActivities.RunCliAgentAsync` + `SimpleAgentWorkflow`, wire
them through a new `/api/temporal/sessions` endpoint, and run one session
end-to-end.

This is the **Phase 1 exit criterion**: if this works, Phase 1 is complete.

**Duration:** ~8 hours.
**Prerequisites:** Day 2 complete (DockerActivities registered + passing tests).

---

## Steps

### Step 1: Create AI contracts

Create `MagicPAI.Activities/Contracts/AiContracts.cs`. Full spec in
`temporal.md` §7.2. Minimum for Day 3:

```csharp
namespace MagicPAI.Activities.Contracts;

public record RunCliAgentInput(
    string Prompt,
    string ContainerId,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string WorkingDirectory = "/workspace",
    string? StructuredOutputSchema = null,
    int MaxTurns = 20,
    int InactivityTimeoutMinutes = 30,
    string? SessionId = null);

public record RunCliAgentOutput(
    string Response,
    string? StructuredOutputJson,
    bool Success,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    IReadOnlyList<string> FilesModified,
    int ExitCode,
    string? AssistantSessionId);
```

Add the other AI contract records for later days (TriageInput/Output,
ClassifierInput/Output, etc.) — stubs for now; implementations in Day 4+.

### Step 2: Create AiActivities.cs with RunCliAgentAsync

Create `MagicPAI.Activities/AI/AiActivities.cs` with the class skeleton from
`temporal.md` §7.8. Full body of `RunCliAgentAsync` included.

For Day 3 we only need `RunCliAgentAsync`. Other methods (TriageAsync, etc.)
can be stubs that throw `NotImplementedException` — stubbed during Day 4+.

### Step 3: Create workflow contracts

Create `MagicPAI.Workflows/Contracts/SimpleAgentContracts.cs`:

```csharp
namespace MagicPAI.Workflows.Contracts;

public record SimpleAgentInput(
    string SessionId,
    string Prompt,
    string AiAssistant,
    string? Model,
    int ModelPower,
    string WorkspacePath,
    bool EnableGui = true,
    IReadOnlyList<string>? EnabledGates = null,
    int MaxCoverageIterations = 3);

public record SimpleAgentOutput(
    string Response,
    bool VerificationPassed,
    int CoverageIterations,
    decimal TotalCostUsd,
    IReadOnlyList<string> FilesModified);
```

### Step 4: Create ActivityProfiles

Create `MagicPAI.Workflows/ActivityProfiles.cs`:

```csharp
using Temporalio.Workflows;

namespace MagicPAI.Workflows;

internal static class ActivityProfiles
{
    public static readonly ActivityOptions Short = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new()
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0,
            NonRetryableErrorTypes = new[] { "ConfigError" }
        }
    };

    public static readonly ActivityOptions Medium = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(15),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        RetryPolicy = new()
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2.0,
            NonRetryableErrorTypes = new[] { "ConfigError", "InvalidPrompt" }
        }
    };

    public static readonly ActivityOptions Long = new()
    {
        StartToCloseTimeout = TimeSpan.FromHours(2),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        CancellationType = ActivityCancellationType.WaitCancellationCompleted,
        RetryPolicy = new()
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(10),
            BackoffCoefficient = 2.0,
            NonRetryableErrorTypes = new[] { "AuthError", "ConfigError", "InvalidPrompt" }
        }
    };

    public static readonly ActivityOptions Container = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(3),
        RetryPolicy = new()
        {
            MaximumAttempts = 1,
            NonRetryableErrorTypes = new[] { "ConfigError" }
        }
    };

    public static readonly ActivityOptions Verify = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(30),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        RetryPolicy = new()
        {
            MaximumAttempts = 2,
            InitialInterval = TimeSpan.FromSeconds(10),
            NonRetryableErrorTypes = new[] { "GateConfigError" }
        }
    };
}
```

### Step 5: Port SimpleAgentWorkflow

Replace `MagicPAI.Server/Workflows/SimpleAgentWorkflow.cs` with the Temporal
version from `temporal.md` §8.4.

**Important:** the old Elsa-based file is being replaced in place. Since Elsa
still needs to work during Phase 1, you have two options:

**Option A:** rename the new Temporal workflow file to avoid collision:
`SimpleAgentWorkflowT.cs` with class `SimpleAgentTemporalWorkflow`. Phase 2 renames
back.

**Option B (recommended):** keep the Temporal version in a new subdir
`MagicPAI.Server/Workflows/Temporal/SimpleAgentWorkflow.cs` with namespace
`MagicPAI.Server.Workflows.Temporal`. Phase 3 merges namespaces.

Using Option B, the file looks like:

```csharp
namespace MagicPAI.Server.Workflows.Temporal;

using Temporalio.Workflows;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Docker;
using MagicPAI.Activities.Verification;     // stub for Day 3; full in Day 5
using MagicPAI.Activities.Contracts;
using MagicPAI.Workflows;
using MagicPAI.Workflows.Contracts;

[Workflow]
public class SimpleAgentWorkflow
{
    // ... full code from §8.4 ...
}
```

For Day 3 simplicity, **omit the coverage loop**. Just:
spawn → run-agent → destroy. Coverage loop added Day 4+ once `AiActivities.GradeCoverageAsync`
and `VerifyActivities.RunGatesAsync` exist.

Minimal Day 3 version:

```csharp
[Workflow]
public class SimpleAgentWorkflow
{
    [WorkflowRun]
    public async Task<SimpleAgentOutput> RunAsync(SimpleAgentInput input)
    {
        var spawn = await Workflow.ExecuteActivityAsync(
            (DockerActivities a) => a.SpawnAsync(new SpawnContainerInput(
                SessionId: input.SessionId,
                WorkspacePath: input.WorkspacePath,
                EnableGui: input.EnableGui)),
            ActivityProfiles.Container);

        try
        {
            var run = await Workflow.ExecuteActivityAsync(
                (AiActivities a) => a.RunCliAgentAsync(new RunCliAgentInput(
                    Prompt: input.Prompt,
                    ContainerId: spawn.ContainerId,
                    AiAssistant: input.AiAssistant,
                    Model: input.Model,
                    ModelPower: input.ModelPower,
                    WorkingDirectory: input.WorkspacePath,
                    SessionId: input.SessionId)),
                ActivityProfiles.Long);

            return new SimpleAgentOutput(
                Response: run.Response,
                VerificationPassed: true,     // verification stub; real in Day 5
                CoverageIterations: 0,
                TotalCostUsd: run.CostUsd,
                FilesModified: run.FilesModified);
        }
        finally
        {
            await Workflow.ExecuteActivityAsync(
                (DockerActivities a) => a.DestroyAsync(new DestroyInput(spawn.ContainerId)),
                ActivityProfiles.Container);
        }
    }
}
```

### Step 6: Register workflow

Update `Program.cs`:
```csharp
.AddScopedActivities<AiActivities>()
.AddWorkflow<MagicPAI.Server.Workflows.Temporal.SimpleAgentWorkflow>();
```

### Step 7: Add /api/temporal/sessions endpoint

Create `MagicPAI.Server/Controllers/TemporalSessionsController.cs` (new,
coexists with existing `SessionController.cs`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;
using MagicPAI.Server.Workflows.Temporal;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Controllers;

[ApiController]
[Route("api/temporal/sessions")]
public class TemporalSessionsController(ITemporalClient temporal) : ControllerBase
{
    public record CreateRequest(
        string Prompt,
        string AiAssistant = "claude",
        string? Model = null,
        int ModelPower = 2,
        string WorkspacePath = "/workspace",
        bool EnableGui = false);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        var workflowId = $"mpai-{Guid.NewGuid():N}";
        var input = new SimpleAgentInput(
            SessionId: workflowId,
            Prompt: req.Prompt,
            AiAssistant: req.AiAssistant,
            Model: req.Model,
            ModelPower: req.ModelPower,
            WorkspacePath: req.WorkspacePath,
            EnableGui: req.EnableGui);

        await temporal.StartWorkflowAsync(
            (SimpleAgentWorkflow wf) => wf.RunAsync(input),
            new(id: workflowId, taskQueue: "magicpai-main"));

        return Accepted(new { SessionId = workflowId });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var handle = temporal.GetWorkflowHandle(id);
        var desc = await handle.DescribeAsync(cancellationToken: ct);
        return Ok(new
        {
            SessionId = id,
            Status = desc.Status.ToString(),
            StartTime = desc.StartTime.ToDateTime(),
            CloseTime = desc.CloseTime?.ToDateTime()
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        await temporal.GetWorkflowHandle(id).CancelAsync();
        return NoContent();
    }
}
```

### Step 8: Build

```powershell
dotnet build
```

Fix any issues. Common ones:
- Missing using directives.
- `Temporalio.Activities.ActivityAttribute` namespace conflicts with Elsa's.

### Step 9: Run unit tests

```powershell
./scripts/run-tests.ps1 Unit
```

Should still pass (new code doesn't break old tests).

### Step 10: Write integration test

Create `MagicPAI.Tests/Workflows/SimpleAgentWorkflowTests.cs`. Use
`WorkflowEnvironment.StartTimeSkippingAsync()` and stub activities. Template in
`temporal.md` §15.4.

Verify:
- Happy-path completes with expected output.
- DestroyAsync called even if RunCliAgent throws.

### Step 11: Capture replay fixture

In the test (temporarily with `[Fact(Skip=null)]`), capture the history:

```csharp
var handle = await _env.Client.StartWorkflowAsync(...);
await handle.GetResultAsync();
var history = await handle.FetchHistoryAsync();
await File.WriteAllTextAsync(
    "Workflows/Histories/simple-agent/happy-path-v1.json",
    history.ToJson());
```

Run once, commit the JSON, then re-add `[Fact(Skip="baseline")]`.

### Step 12: Write replay test

Create `MagicPAI.Tests/Workflows/SimpleAgentReplayTests.cs`:

```csharp
using Temporalio.Worker;
using MagicPAI.Server.Workflows.Temporal;

public class SimpleAgentReplayTests
{
    [Theory]
    [InlineData("Workflows/Histories/simple-agent/happy-path-v1.json")]
    [Trait("Category", "Replay")]
    public async Task ReplaysSuccessfully(string historyPath)
    {
        var history = WorkflowHistory.FromJson(
            workflowId: "replay",
            json: await File.ReadAllTextAsync(historyPath));
        var result = await new WorkflowReplayer(new(typeof(SimpleAgentWorkflow)))
            .ReplayWorkflowAsync(history);
        result.Successful.Should().BeTrue();
    }
}
```

### Step 13: Start full stack

```powershell
./scripts/dev-up.ps1
```

### Step 14: Manual smoke test via curl

```powershell
$resp = Invoke-RestMethod -Uri http://localhost:5000/api/temporal/sessions `
    -Method Post -ContentType 'application/json' `
    -Body '{ "prompt": "print hello", "aiAssistant": "claude", "model": "haiku", "modelPower": 3 }'
$sid = $resp.sessionId
Write-Host "Session: $sid"

# Watch in Temporal UI
Start-Process "http://localhost:8233/namespaces/magicpai/workflows/$sid"

# Poll status
while ($true) {
    $s = (Invoke-RestMethod http://localhost:5000/api/temporal/sessions/$sid).status
    Write-Host "Status: $s"
    if ($s -in @("Completed","Failed","Cancelled","Terminated")) { break }
    Start-Sleep 5
}
```

### Step 15: Verify visually

- Temporal UI shows clean event history: Started → ActivityScheduled (SpawnAsync)
  → Completed → ActivityScheduled (RunCliAgentAsync) → Completed →
  ActivityScheduled (DestroyAsync) → Completed → WorkflowCompleted.
- No red errors.
- `docker ps` after completion: no orphan `magicpai-session-*` containers.

### Step 16: Commit

```powershell
git add .
git commit -m "temporal: Phase 1 complete — SimpleAgentWorkflow runs end-to-end via Temporal

- AiActivities with RunCliAgentAsync ported.
- SimpleAgentWorkflow in new Temporal namespace; coexists with Elsa.
- /api/temporal/sessions endpoint.
- Integration tests + replay fixture.
- Phase 1 exit criteria met."
git tag v2.0.0-phase1
```

### Step 17: Update SCORECARD

Mark every Phase 1 checkbox complete. Sign off.

---

## Definition of done (Phase 1 complete)

- [ ] `dotnet build` zero warnings.
- [ ] `dotnet test` all pass including replay.
- [ ] `POST /api/temporal/sessions` creates a session that completes.
- [ ] SignalR streams output in browser (if browser connected to hub).
- [ ] Cancel via DELETE works and destroys container.
- [ ] Temporal UI shows clean event history.
- [ ] No orphan containers after run.
- [ ] Tag `v2.0.0-phase1` created.
- [ ] SCORECARD.md Phase 1 section all checked off.
- [ ] Demo shown to team.

## Troubleshooting

**Workflow registered but activity not found:**
`ActivityNotRegisteredException`. Check `Program.cs` has `AddScopedActivities<AiActivities>()`.

**Container spawn fails:**
```powershell
docker logs mpai-server | tail -50
```
Common: Docker socket not mounted; host path incorrect for `~/.claude`.

**Stream doesn't reach browser:**
SignalR client not subscribed to session. Pre-Phase-1 MagicPAI.Studio used Elsa's
hub. For Day 3 manual test, just verify `docker logs mpai-server` shows the
activity streaming.

**Replay test fails with non-determinism:**
Check workflow code for `DateTime.UtcNow` etc. See §25 anti-patterns.

## Next

Phase 2 Day 4 — remaining AI activities (Triage, Classify, Architect, etc.).

See `docs/phase-guides/Phase2-Day4.md`.
