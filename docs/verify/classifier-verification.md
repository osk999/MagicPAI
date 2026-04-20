# Classifier Activity Live E2E Verification

**Date:** 2026-04-20
**Branch:** `temporal`
**Scope:** `AiActivities.TriageAsync`, `AiActivities.ClassifyAsync`,
`AiActivities.ClassifyWebsiteTaskAsync` in
`MagicPAI.Activities/AI/AiActivities.cs`.

**Verdict:** PASS — all three classifiers run inside a real Docker container
and produce Claude-authored structured JSON (no static fallbacks).

---

## Step 1 — Code Audit

All three methods are present, correctly structured, and correctly guarded:

| Activity | File:line | Guard | Schema | Docker call |
|---|---|---|---|---|
| `TriageAsync` | `MagicPAI.Activities/AI/AiActivities.cs:246-326` | Throws `ApplicationFailureException(errorType="ConfigError", nonRetryable:true)` when `ContainerId` empty | `SchemaGenerator.FromType<TriageResult>()` (line 258) → `AgentRequest.OutputSchema` | `_docker.ExecAsync(input.ContainerId, plan.MainRequest, ct)` (line 277) |
| `ClassifyAsync` | `MagicPAI.Activities/AI/AiActivities.cs:334-415` | Same ConfigError guard on empty `ContainerId` | Inline yes/no JSON schema: `{result:boolean, confidence:number, rationale:string}` (line 351-353) | `_docker.ExecAsync(input.ContainerId, plan.MainRequest, ct)` (line 374) |
| `ClassifyWebsiteTaskAsync` | `MagicPAI.Activities/AI/AiActivities.cs:787-801` | Inherits via delegation | Inherits via delegation | Delegates to `ClassifyAsync` with a fixed question |

### SessionId fix confirmed

Grep across `AiActivities.cs` for the old bug pattern `SessionId = input.SessionId`:

```
(no matches)
```

Grep for the fix (`SessionId = null`):

```
line 266 — TriageAsync
line 363 — ClassifyAsync
line 481 — EnhancePromptAsync
line 572 — ArchitectAsync
line 681 — ResearchPromptAsync
line 848 — GradeCoverageAsync
```

All 6 AI activities that shouldn't resume a Claude CLI session correctly pass
`SessionId = null` to the `AgentRequest`. Only `RunCliAgentAsync` (line 104)
propagates `input.AssistantSessionId` — which is the right behavior because
only `RunCliAgent` is supposed to continue an existing Claude CLI session via
`--resume`.

---

## Step 2 — Live E2E Test Harness

Test file:
[`MagicPAI.Tests/Activities/Live/ClassifierLiveDockerTests.cs`](../../MagicPAI.Tests/Activities/Live/ClassifierLiveDockerTests.cs)

Design choices:

- Placed in `MagicPAI.Tests` rather than `MagicPAI.Tests.Integration` because
  the latter transitively pulls `Docker.DotNet.Enhanced 3.131` through
  `Testcontainers.PostgreSql`. That fork ships an `Docker.DotNet.dll` with an
  incompatible `DockerClientConfiguration.CreateClient` signature, which
  collides with the `Docker.DotNet 3.125` copy the production
  `DockerContainerManager` is compiled against. Running the test there would
  fail at runtime with `MissingMethodException`.
- Uses the real `DockerContainerManager`, real `CliAgentFactory` (Claude
  runner), real `AuthRecoveryService` (unused path — no auth error triggered
  in the happy path), and a null `ISessionStreamSink`.
- `IAsyncLifetime` spawns one `magicpai-env:latest` container per test
  instance and destroys it on teardown. Each `[SkippableFact]` verifies the
  container status before and after the call.
- Waits up to 90s for the entrypoint's credential sync to land
  (`/home/worker/.claude/.credentials.json` or `~/.claude.json`) before
  letting the test call Claude.
- Skips (not fails) when host credentials are missing; runs unconditionally
  when they are present.
- `[Trait("Category", "ClassifierE2E")]` for targeted filtering.

### Packages added

- `MagicPAI.Tests.csproj`: `Xunit.SkippableFact 1.5.23`.

No packages or code were added to `MagicPAI.Tests.Integration` after the
initial attempt was rolled back due to the Docker.DotNet version collision.

---

## Step 3 — Test Execution

Command:

```powershell
dotnet test MagicPAI.Tests/MagicPAI.Tests.csproj -c Release --no-build `
  --filter "Category=ClassifierE2E" --logger "console;verbosity=detailed"
```

Result:

```
Total tests: 4
     Passed: 4
 Total time: 49.5861 Seconds
```

Per-test:

| Test | Result | Time |
|---|---|---|
| `AllClassifiers_ContainerIdMissing_ThrowsConfigErrorWithoutInvokingDocker` | PASS | 66 ms |
| `ClassifyAsync_Live_ProducesBooleanAnswerFromClaude` | PASS | 9.6 s |
| `TriageAsync_Live_ProducesStructuredTriageFromClaude` | PASS | 16.6 s |
| `ClassifyWebsiteTaskAsync_Live_DelegatesToClassifyAndReturnsWebsiteFlag` | PASS | 18.7 s |

### Captured Claude output

```
Triage ⇒ complexity=8 category=refactor power=1 model=opus
          needsDecomp=True isComplex=True

Classify ⇒ result=True confidence=0.98
           rationale="The content describes a NullReferenceException being
           thrown when clicking the login button on the dashboard page…"

ClassifyWebsiteTask ⇒ isWebsite=True confidence=0.99
           rationale="The task explicitly involves building a React web
           application with UI/UX components (hero section, pricing cards,
           sticky navigation bar) styled with Tailwind CSS…"
```

None of these values match the static-fallback tuples
(Triage `(5, "code_gen", 2, false)`,
Classify/WebsiteClassify `(false, 0, "parse-failure")`).

---

## Step 4 — Docker Involvement

Every live test logs its spawned `containerId`, `ExecAsync` called against
that container, and its post-destroy status:

```
Spawned container 258293da…f02 on C:\…\magicpai-classifier-e2e-1babe5c8
Pre-triage container status: running
Triage (16.6s) => complexity=8 category=refactor power=1 …
Destroyed container 258293da…f02
Post-destroy status: not-found
```

All 4 containers (one per test) show `Post-destroy status: not-found`
— no orphans, clean lifecycle.

Docker interaction is confirmed at three layers:

1. **SpawnAsync** — The real `docker create` / `docker start` path in
   `DockerContainerManager.SpawnAsync` produced a container ID of the
   expected 64-char hex format.
2. **ExecAsync** — Classifier activities call `_docker.ExecAsync(...)` with
   the `ContainerExecRequest` produced by `ClaudeRunner.BuildExecutionPlan`.
   The Claude CLI process ran inside the container (confirmed by non-static
   output + non-trivial latency).
3. **DestroyAsync** — `IAsyncLifetime.DisposeAsync` calls
   `_docker.DestroyAsync`, and a follow-up `docker inspect` reports the
   container is gone (`not-found`).

The `AllClassifiers_ContainerIdMissing_…` test also verifies Docker is
**never** called when `ContainerId` is empty — all three methods throw
`ConfigError` before reaching any Docker call.

---

## Step 5 — Structured Output Validation

Asserted for the happy-path tests:

### Triage

| Assertion | Result |
|---|---|
| `Complexity ∈ [1, 10]` | 8 ✅ |
| `Category ∈ {code_gen, bug_fix, refactor, architecture, testing, docs}` | `refactor` ✅ |
| `RecommendedModel` non-empty | `opus` ✅ |
| `Complexity ≥ 4` (sanity for a cross-module refactor) | 8 ✅ |
| Not the fallback `(5, "code_gen", 2, false)` | ✅ (value is `(8, "refactor", 1, true)`) |
| `IsComplex == (Complexity ≥ ComplexityThreshold)` | 8 ≥ 7 → `True` ✅ |

### Classify

| Assertion | Result |
|---|---|
| `Rationale ≠ "parse-failure"` | Long substantive rationale ✅ |
| `Rationale` non-empty | ✅ |
| `Result == true` for a clear bug prompt | `True` ✅ |
| `Confidence > 0m` | `0.98` ✅ |

### ClassifyWebsiteTask

| Assertion | Result |
|---|---|
| `Rationale ≠ "parse-failure"` | Long substantive rationale ✅ |
| `Rationale` non-empty | ✅ |
| `IsWebsiteTask == true` for a React/Tailwind landing-page prompt | `True` ✅ |
| `Confidence > 0m` | `0.99` ✅ |

---

## Conclusion

All three Temporal classifier activities in
`MagicPAI.Activities/AI/AiActivities.cs`:

1. Run inside a real `magicpai-env:latest` Docker container (spawn → exec →
   destroy lifecycle observed).
2. Invoke `_docker.ExecAsync` with the plan built by `ClaudeRunner`.
3. Return Claude-authored structured JSON that matches the declared
   `OutputSchema`, not the static-fallback tuples.
4. Correctly guard the required `ContainerId` input and throw a
   non-retryable `ConfigError` `ApplicationFailureException` when it is
   missing.

The previously-fixed `SessionId = null` in all six non-`RunCliAgent` AI
activities is still in place (see Step 1).

The test suite
[`ClassifierLiveDockerTests`](../../MagicPAI.Tests/Activities/Live/ClassifierLiveDockerTests.cs)
can be re-run locally with:

```powershell
dotnet test MagicPAI.Tests/MagicPAI.Tests.csproj -c Release `
  --filter "Category=ClassifierE2E"
```
