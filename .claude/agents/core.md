---
name: core
description: Build MagicPAI.Core — shared models, interfaces, and services (runners, gates, Docker, blackboard, auth). Engine-agnostic; NOT Temporal-specific.
isolation: worktree
---

You are working on **MagicPAI.Core** — the engine-agnostic foundation of the system.

## Your scope (ONLY touch these files)
- `MagicPAI.Core/**`

## Critical principle

**MagicPAI.Core must stay engine-agnostic.** Temporal lives in `MagicPAI.Activities`, `MagicPAI.Workflows`, and `MagicPAI.Server`. Core has ZERO references to `Temporalio.*` (and previously, no `Elsa.*` either). This is by design — it's what allowed the Elsa→Temporal migration to happen with Core unchanged.

**Core was not modified during the Elsa→Temporal migration.** If you're asked to add Temporal-specific logic to Core, STOP and verify — it almost certainly belongs in `MagicPAI.Activities` or `MagicPAI.Server` instead.

## What lives in Core

- `Services/ClaudeRunner.cs`, `CodexRunner.cs`, `GeminiRunner.cs` — CLI runners (implement `ICliAgentRunner`).
- `Services/CliAgentFactory.cs` — picks a runner by name.
- `Services/DockerContainerManager.cs`, `KubernetesContainerManager.cs`, `LocalContainerManager.cs` — container lifecycle (`IContainerManager`).
- `Services/Gates/*` — verification gates (implement `IVerificationGate`).
- `Services/VerificationPipeline.cs` — runs a set of gates.
- `Services/SharedBlackboard.cs` — in-memory file claims + task outputs.
- `Services/Auth/*` — `AuthRecoveryService`, `AuthErrorDetector`, `CredentialInjector`.
- `Services/ISessionStreamSink.cs` — side-channel interface for SignalR streaming (engine-agnostic contract; impl lives in Server).
- `Config/MagicPaiConfig.cs` — configuration record.
- `Models/*` — plain records (e.g., `SessionInfo`, `TriageResult`, `AgentRequest`, `AgentResponse`, `ContainerConfig`).

## Core interface contracts (must be preserved exactly)

- `ICliAgentRunner` — `BuildExecutionPlan(AgentRequest)`, `ParseResponse(string)`, `AgentName`, `DefaultModel`, `AvailableModels`, `SupportsNativeSchema`.
- `ICliAgentFactory` — `Create(string agentName)`, `AvailableAgents`.
- `IContainerManager` — `SpawnAsync`, `ExecAsync`, `ExecStreamingAsync` (callback-based, not IAsyncEnumerable), `DestroyAsync`, `IsRunningAsync`.
- `IVerificationGate` — `Name`, `IsBlocking`, `CanVerifyAsync`, `VerifyAsync`.
- `IExecutionEnvironment` — `RunCommandAsync`, `StartProcessAsync`, `Kind`.
- `ISessionStreamSink` — `EmitChunkAsync`, `EmitStructuredAsync`, `EmitStageAsync`, `CompleteSessionAsync`.
- `IStartupValidator` — `Validate()`.

## Do not introduce

- Anything that pulls in `Temporalio.*`.
- EF Core / SignalR / ASP.NET Core dependencies (those belong in Server).
- Blazor (belongs in Studio).

## When Core legitimately changes

Only when adding new engine-agnostic capability — e.g.:
- A new `IVerificationGate` implementation.
- A new `ICliAgentRunner` for a new AI provider.
- New auth helpers.

For each change:
1. Add unit tests in `MagicPAI.Tests/` (no engine-specific dependencies).
2. Build: `dotnet build MagicPAI.Core/MagicPAI.Core.csproj` — 0 errors, 0 warnings.
3. `dotnet test MagicPAI.Tests --filter "Category=Unit"` — all pass.

## Specifications

See `temporal.md` (Appendix QQ.1) for the Core change policy: "No changes" outside of legitimate new Core capabilities.

Also see the "Interface Contracts (must be implemented exactly)" section in `CLAUDE.md`.
