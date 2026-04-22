# Phase 1 — Day 2: First activity group (DockerActivities)

**Objective:** port the 4 Docker activities into a single `DockerActivities`
class. First real Temporal activity code in the repo.

**Duration:** ~6 hours.
**Prerequisites:** Day 1 complete (SCORECARD items all checked).

---

## Steps

### Step 1: Create contracts file

Create `MagicPAI.Activities/Contracts/DockerContracts.cs`:

```csharp
namespace MagicPAI.Activities.Contracts;

public record SpawnContainerInput(
    string SessionId,
    string Image = "magicpai-env:latest",
    string WorkspacePath = "",
    int MemoryLimitMb = 4096,
    bool EnableGui = false,
    Dictionary<string, string>? EnvVars = null);

public record SpawnContainerOutput(
    string ContainerId,
    string? GuiUrl);

public record ExecInput(
    string ContainerId,
    string Command,
    string WorkingDirectory = "/workspace",
    int TimeoutSeconds = 600);

public record ExecOutput(
    int ExitCode,
    string Output,
    string? Error);

public record StreamInput(
    string ContainerId,
    string Command,
    string WorkingDirectory = "/workspace",
    int TimeoutMinutes = 120,
    string? SessionId = null);

public record StreamOutput(
    int ExitCode,
    int LineCount,
    string? SummaryLine);

public record DestroyInput(
    string ContainerId,
    bool ForceKill = false);
```

Full spec in `temporal.md` §7.3.

### Step 2: Create ISessionStreamSink in Core

Create `MagicPAI.Core/Services/ISessionStreamSink.cs`:

```csharp
namespace MagicPAI.Core.Services;

public interface ISessionStreamSink
{
    Task EmitChunkAsync(string sessionId, string line, CancellationToken ct);
    Task EmitStructuredAsync(string sessionId, string eventName, object payload, CancellationToken ct);
    Task EmitStageAsync(string sessionId, string stage, CancellationToken ct);
    Task CompleteSessionAsync(string sessionId, CancellationToken ct);
}
```

### Step 3: Create DockerActivities.cs

Create `MagicPAI.Activities/Docker/DockerActivities.cs` per the template in
`temporal.md` §7.7. Summary:

```csharp
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Exceptions;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Docker;

public class DockerActivities
{
    private readonly IContainerManager _docker;
    private readonly IGuiPortAllocator? _guiPort;
    private readonly ISessionContainerRegistry? _registry;
    private readonly ISessionContainerLogStreamer? _logStreamer;
    private readonly ISessionStreamSink _sink;
    private readonly MagicPaiConfig _config;
    private readonly ILogger<DockerActivities> _log;

    public DockerActivities(
        IContainerManager docker, ISessionStreamSink sink,
        MagicPaiConfig config, ILogger<DockerActivities> log,
        IGuiPortAllocator? guiPort = null,
        ISessionContainerRegistry? registry = null,
        ISessionContainerLogStreamer? logStreamer = null)
    {
        _docker = docker; _sink = sink; _config = config; _log = log;
        _guiPort = guiPort; _registry = registry; _logStreamer = logStreamer;
    }

    [Activity]
    public async Task<SpawnContainerOutput> SpawnAsync(SpawnContainerInput input)
    {
        // ... full body in §7.7 ...
    }

    [Activity]
    public async Task<ExecOutput> ExecAsync(ExecInput input) { /* ... */ }

    [Activity]
    public async Task<StreamOutput> StreamAsync(StreamInput input) { /* ... */ }

    [Activity]
    public async Task DestroyAsync(DestroyInput input) { /* ... */ }
}
```

Copy the full 4 method bodies from `temporal.md` §7.7.

### Step 4: Create SignalRSessionStreamSink

Create `MagicPAI.Server/Services/SignalRSessionStreamSink.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MagicPAI.Core.Services;
using MagicPAI.Server.Hubs;
using MagicPAI.Shared.Hubs;

namespace MagicPAI.Server.Services;

public class SignalRSessionStreamSink(
    IHubContext<SessionHub, ISessionHubClient> hub,
    ILogger<SignalRSessionStreamSink> log) : ISessionStreamSink
{
    public Task EmitChunkAsync(string sessionId, string line, CancellationToken ct) =>
        hub.Clients.Group(sessionId).OutputChunk(line);

    public Task EmitStructuredAsync(string sessionId, string eventName, object payload, CancellationToken ct) =>
        hub.Clients.Group(sessionId).StructuredEvent(eventName, payload);

    public Task EmitStageAsync(string sessionId, string stage, CancellationToken ct) =>
        hub.Clients.Group(sessionId).StageChanged(stage);

    public Task CompleteSessionAsync(string sessionId, CancellationToken ct) =>
        hub.Clients.Group(sessionId).SessionCompleted(new SessionCompletedPayload(
            SessionId: sessionId,
            WorkflowType: "",
            CompletedAt: DateTime.UtcNow,
            TotalCostUsd: 0,
            Result: null));
}
```

(Requires `MagicPAI.Shared/Hubs/ISessionHubClient.cs` from temporal.md §J.1 — create if not yet present.)

### Step 5: Register DI in Program.cs

Add to `MagicPAI.Server/Program.cs` (alongside existing Elsa wiring — do not remove anything yet):

```csharp
// Temporal client (skip activation/worker for now; just client)
builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = builder.Configuration["Temporal:Host"] ?? "localhost:7233";
    opts.Namespace  = builder.Configuration["Temporal:Namespace"] ?? "magicpai";
});

// Temporal worker
builder.Services
    .AddHostedTemporalWorker(
        clientTargetHost: builder.Configuration["Temporal:Host"] ?? "localhost:7233",
        clientNamespace: builder.Configuration["Temporal:Namespace"] ?? "magicpai",
        taskQueue: "magicpai-main")
    .AddScopedActivities<DockerActivities>();

// Stream sink
builder.Services.AddSingleton<ISessionStreamSink, SignalRSessionStreamSink>();
```

### Step 6: Add Temporal config to appsettings.json

Add to `appsettings.json`:
```json
"Temporal": {
  "Host": "localhost:7233",
  "Namespace": "magicpai",
  "TaskQueue": "magicpai-main",
  "UiBaseUrl": "http://localhost:8233"
}
```

### Step 7: Build

```powershell
dotnet build
```

Expected: success. Any errors? Usually:
- Missing using directive: add it.
- Type conflicts between Elsa `[Activity]` and Temporalio `[Activity]`:
  use fully-qualified names `Temporalio.Activities.ActivityAttribute`.

### Step 8: Write unit tests

Create `MagicPAI.Tests/Activities/DockerActivitiesTests.cs` with at least:
- `SpawnAsync_ReturnsContainerId_WhenDockerAvailable`
- `ExecAsync_ThrowsApplicationFailure_OnException`
- `StreamAsync_HeartbeatsPeriodically`
- `DestroyAsync_Idempotent_WhenContainerAlreadyGone`

Use `ActivityEnvironment` from `Temporalio.Testing` + Moq for `IContainerManager`.
See `temporal.md` §15.3.

### Step 9: Run tests

```powershell
./scripts/run-tests.ps1 Unit
```

Expected: all green.

### Step 10: Run the server

```powershell
dotnet run --project MagicPAI.Server
```

Expected logs:
- Elsa workflow runtime starting (unchanged).
- Temporal worker connecting to localhost:7233.
- Temporal worker registered activities: SpawnAsync, ExecAsync, StreamAsync, DestroyAsync.

### Step 11: Quick manual verification

In another shell:
```powershell
./scripts/temporal-cli.ps1 task-queue describe --task-queue magicpai-main
```

Expected: shows one worker pollers with 4 activity types.

### Step 12: Commit

```powershell
git add MagicPAI.Activities/Contracts/DockerContracts.cs
git add MagicPAI.Activities/Docker/DockerActivities.cs
git add MagicPAI.Core/Services/ISessionStreamSink.cs
git add MagicPAI.Server/Services/SignalRSessionStreamSink.cs
git add MagicPAI.Server/Program.cs
git add MagicPAI.Server/appsettings.json
git add MagicPAI.Tests/Activities/DockerActivitiesTests.cs
git commit -m "temporal: port DockerActivities (4 methods)

- SpawnAsync, ExecAsync, StreamAsync, DestroyAsync ported as Temporal [Activity] methods.
- ISessionStreamSink side-channel for SignalR-backed streaming.
- DI wired alongside Elsa (coexistence).
- Unit tests with ActivityEnvironment + mocked IContainerManager."
```

### Step 13: Update SCORECARD

Check off in Phase 1 Code section:
- [x] `MagicPAI.Activities/Contracts/DockerContracts.cs`
- [x] `MagicPAI.Server/Services/SignalRSessionStreamSink.cs`

---

## Definition of done

- [ ] `dotnet build` — zero warnings/errors.
- [ ] `dotnet test --filter Category=Unit` — passes (including new DockerActivitiesTests).
- [ ] Server starts with no exceptions; Temporal worker logs connected.
- [ ] `temporal task-queue describe` shows the worker with 4 activities registered.
- [ ] Commit created and pushed (or held for review).
- [ ] SCORECARD.md updated.

## Troubleshooting

**Temporal.Activities namespace conflicts with Elsa.Workflows:**
Use alias: `using TemporalActivity = Temporalio.Activities.ActivityAttribute;`. Prefer
disambiguating at the attribute site: `[Temporalio.Activities.Activity]`.

**Worker keeps restarting:**
Check `docker ps` — is `mpai-temporal` healthy? Check logs: `docker logs mpai-server`.

**ISessionStreamSink — hub not found:**
`SessionHub` is the existing hub. Ensure `MagicPAI.Shared.Hubs.ISessionHubClient`
interface exists (create per §J.1 if missing).

## Next

`docs/phase-guides/Phase1-Day3.md` — port `AiActivities.RunCliAgentAsync`, then
`SimpleAgentWorkflow`, full E2E.
