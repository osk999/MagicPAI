---
name: core
description: Build MagicPAI.Core — all models, interfaces, and services
isolation: worktree
---

You are building the **MagicPAI.Core** project — the shared library that all other projects depend on.

## Your Scope (ONLY touch these files)
- `MagicPAI.Core/**`

## What to Build

Read `MAGICPAI_PLAN.md` for detailed specifications. Build in this exact order:

### Step 1: Models (MagicPAI.Core/Models/)
- `CliAgentResponse.cs` — record with Success, Output, CostUsd, FilesModified, InputTokens, OutputTokens, SessionId
- `VerificationResult.cs` — PipelineResult with Gates list, AllPassed, IsInconclusive  
- `GateResult.cs` — record with Name, Passed, Output, Issues, Duration
- `TriageResult.cs` — record with Complexity, Category, RecommendedModel, NeedsDecomposition
- `ContainerConfig.cs` — Image, WorkspacePath, MemoryLimitMb, CpuCount, EnableGui, Env, Timeout
- `ContainerInfo.cs` — record with ContainerId, GuiUrl
- `ExecResult.cs` — record with ExitCode, Output, Error

### Step 2: Interfaces (MagicPAI.Core/Services/)
- `ICliAgentRunner.cs` — AgentName, DefaultModel, AvailableModels, BuildCommand(), ParseResponse()
- `ICliAgentFactory.cs` — Create(agentName), AvailableAgents
- `IContainerManager.cs` — SpawnAsync(), ExecAsync(), ExecStreamingAsync(), DestroyAsync(), IsRunningAsync()
- `IVerificationGate.cs` — Name, IsBlocking, CanVerifyAsync(), VerifyAsync()
- `IExecutionEnvironment.cs` — RunCommandAsync(), Kind

### Step 3: Services
- `ClaudeRunner.cs` — implements ICliAgentRunner. BuildCommand with --dangerously-skip-permissions --output-format stream-json. ParseResponse parses stream-json. See MAGICPAI_PLAN.md Section 10.3.
- `CodexRunner.cs` — implements ICliAgentRunner. See MAGICPAI_PLAN.md Section 14.2.
- `GeminiRunner.cs` — implements ICliAgentRunner. See MAGICPAI_PLAN.md Section 14.3.
- `CliAgentFactory.cs` — implements ICliAgentFactory. Factory pattern for claude/codex/gemini.
- `DockerContainerManager.cs` — implements IContainerManager using Docker.DotNet. See MAGICPAI_PLAN.md Section 9.3.
- `LocalExecutionEnvironment.cs` — fallback when Docker is disabled.
- `SharedBlackboard.cs` — ConcurrentDictionary-based file claims. See MAGICPAI_PLAN.md Section 15.2.
- `WorktreeManager.cs` — git worktree create/merge/cleanup via shell commands.
- `VerificationPipeline.cs` — chains IVerificationGate instances with early-stop. See MAGICPAI_PLAN.md Section 11.3.

### Step 4: Verification Gates (MagicPAI.Core/Services/Gates/)
- `CompileGate.cs` — detects project type, runs build command. IsBlocking = true.
- `TestGate.cs` — detects test framework, runs tests. IsBlocking = true.
- `CoverageGate.cs` — checks line coverage threshold. IsBlocking = true.
- `SecurityGate.cs` — regex-based security pattern scan. IsBlocking = false.
- `LintGate.cs` — style checks. IsBlocking = false.
- `HallucinationDetector.cs` — verifies claimed files exist on disk. IsBlocking = true.
- `QualityReviewGate.cs` — static analysis patterns. IsBlocking = false.

### Step 5: Config
- `MagicPaiConfig.cs` — ~50 properties. See MAGICPAI_PLAN.md Section 19.1.

## Rules
- Run `dotnet build MagicPAI.Core/MagicPAI.Core.csproj` after completing each step
- Fix any build errors before moving to the next step
- This project has NO dependency on Elsa packages — only Docker.DotNet and System.Text.Json
