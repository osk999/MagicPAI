# MagicPAI — Comprehensive Test Plan

> **Goal**: 100% coverage of all use cases, edge cases, and integration scenarios.
> **Agents under test**: Claude Code, OpenAI Codex CLI, Gemini CLI.
> **Key focus areas**: (1) Real CLI agent execution on machine, (2) JSON Schema structured output (classifiers), (3) Streaming output (never buffer full result).

---

## Table of Contents

1. [Unit Tests — Core Services](#1-unit-tests--core-services)
2. [Unit Tests — Verification Gates](#2-unit-tests--verification-gates)
3. [Unit Tests — Activities](#3-unit-tests--activities)
4. [Unit Tests — Models & DTOs](#4-unit-tests--models--dtos)
5. [Unit Tests — Server Components](#5-unit-tests--server-components)
6. [Unit Tests — Workflows](#6-unit-tests--workflows)
7. [Integration Tests — Docker](#7-integration-tests--docker)
8. [Integration Tests — Real Agent Execution](#8-integration-tests--real-agent-execution)
9. [Integration Tests — Structured Output / Classifiers](#9-integration-tests--structured-output--classifiers)
10. [Integration Tests — Streaming](#10-integration-tests--streaming)
11. [Integration Tests — SignalR Real-Time](#11-integration-tests--signalr-real-time)
12. [Integration Tests — REST API](#12-integration-tests--rest-api)
13. [Integration Tests — End-to-End Workflows](#13-integration-tests--end-to-end-workflows)
14. [Edge Cases & Error Scenarios](#14-edge-cases--error-scenarios)
15. [Performance & Stress Tests](#15-performance--stress-tests)
16. [Security Tests](#16-security-tests)
17. [Configuration Tests](#17-configuration-tests)
18. [Docker Infrastructure Tests](#18-docker-infrastructure-tests)
19. [Blazor Studio Frontend Tests](#19-blazor-studio-frontend-tests)

---

## 1. Unit Tests — Core Services

### 1.1 ClaudeRunner

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.1.1 | `AgentName_ReturnsClaude` | Property returns "claude" | "claude" |
| 1.1.2 | `DefaultModel_ReturnsSonnet` | Property returns "sonnet" | "sonnet" |
| 1.1.3 | `AvailableModels_ContainsHaikuSonnetOpus` | All three models listed | ["haiku","sonnet","opus"] |
| 1.1.4 | `BuildCommand_IncludesDangerouslySkipPermissions` | Flag present in output | Contains `--dangerously-skip-permissions` |
| 1.1.5 | `BuildCommand_IncludesStreamJsonFormat` | Output format set | Contains `--output-format stream-json` |
| 1.1.6 | `BuildCommand_ResolvesHaikuModel` | haiku -> haiku-4-5-20251001 | Contains `claude-haiku-4-5-20251001` |
| 1.1.7 | `BuildCommand_ResolvesSonnetModel` | sonnet -> sonnet-4-6-20250627 | Contains `claude-sonnet-4-6-20250627` |
| 1.1.8 | `BuildCommand_ResolvesOpusModel` | opus -> opus-4-6-20250627 | Contains `claude-opus-4-6-20250627` |
| 1.1.9 | `BuildCommand_ResolvesUnknownModelAsLiteral` | "custom-v1" passed through | Contains `claude-custom-v1` |
| 1.1.10 | `BuildCommand_EscapesSingleQuotesInPrompt` | Prompt with `'` escaped | No broken quotes |
| 1.1.11 | `BuildCommand_IncludesMaxTurns` | Max turns param set | Contains `--max-turns 15` |
| 1.1.12 | `BuildCommand_IncludesWorkDir` | cd to work dir | Starts with `cd /workspace &&` |
| 1.1.13 | `BuildCommand_HandlesEmptyPrompt` | Empty string prompt | Valid command (no crash) |
| 1.1.14 | `BuildCommand_HandlesNewlinesInPrompt` | Multi-line prompt | Escaped correctly |
| 1.1.15 | `BuildCommand_HandlesSpecialCharsInPrompt` | `$`, backticks, `\` in prompt | Properly escaped |
| 1.1.16 | `ParseResponse_ValidResultJson` | Standard result JSON | Success=true, extracts all fields |
| 1.1.17 | `ParseResponse_ErrorResult` | JSON with is_error=true | Success=false |
| 1.1.18 | `ParseResponse_MultipleJsonLines` | Stream with many JSON lines | Takes last "result" type |
| 1.1.19 | `ParseResponse_NoResultLine` | Output with no "type":"result" | Success=false, raw output preserved |
| 1.1.20 | `ParseResponse_MalformedJson` | Invalid JSON | Success=false, returns raw |
| 1.1.21 | `ParseResponse_EmptyOutput` | Empty string | Success=false |
| 1.1.22 | `ParseResponse_ExtractsCostUsd` | JSON with cost_usd | CostUsd matches |
| 1.1.23 | `ParseResponse_MissingCostUsd` | No cost field | CostUsd=0 |
| 1.1.24 | `ParseResponse_ExtractsFilesModified` | JSON with files array | Files extracted |
| 1.1.25 | `ParseResponse_EmptyFilesArray` | Empty files_modified | Empty array |
| 1.1.26 | `ParseResponse_MissingFilesField` | No files_modified | Empty array |
| 1.1.27 | `ParseResponse_ExtractsTokenUsage` | usage.input_tokens + output_tokens | Both extracted |
| 1.1.28 | `ParseResponse_MissingUsage` | No usage block | 0,0 |
| 1.1.29 | `ParseResponse_ExtractsSessionId` | session_id present | Extracted |
| 1.1.30 | `ParseResponse_MissingSessionId` | No session_id | null |
| 1.1.31 | `ParseResponse_MixedOutputAndResult` | Non-JSON lines + JSON lines | Correctly ignores non-JSON |
| 1.1.32 | `ParseResponse_NullFilesInArray` | files_modified with null entries | Filtered out |
| 1.1.33 | `ParseResponse_LargeOutput` | 100MB+ output | No OOM, parses correctly |

### 1.2 CodexRunner

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.2.1 | `AgentName_ReturnsCodex` | Property check | "codex" |
| 1.2.2 | `DefaultModel_ReturnsO3` | Default model | "o3" |
| 1.2.3 | `AvailableModels_ContainsO3O4MiniGpt4o` | All models | ["o3","o4-mini","gpt-4o"] |
| 1.2.4 | `BuildCommand_IncludesApprovalModeFullAuto` | Auto-approval flag | Contains `--approval-mode full-auto` |
| 1.2.5 | `BuildCommand_IncludesModel` | Model param | Contains `-m o3` |
| 1.2.6 | `BuildCommand_EscapesSingleQuotes` | Quote in prompt | Properly escaped |
| 1.2.7 | `BuildCommand_IncludesWorkDir` | cd to workspace | Starts with `cd /workspace &&` |
| 1.2.8 | `ParseResponse_SuccessfulOutput` | Clean output | Success=true |
| 1.2.9 | `ParseResponse_ErrorInOutput` | Output contains "error" | Success=false |
| 1.2.10 | `ParseResponse_CaseInsensitiveErrorDetection` | "ERROR" uppercase | Success=false |
| 1.2.11 | `ParseResponse_ErrorInUnrelatedContext` | "no errors found" | Success=false (known limitation — tests this) |
| 1.2.12 | `ParseResponse_NoCostExtraction` | Any output | CostUsd=0 always |
| 1.2.13 | `ParseResponse_NoTokenExtraction` | Any output | InputTokens=0, OutputTokens=0 |
| 1.2.14 | `ParseResponse_EmptyOutput` | Empty string | Success=true (no "error" found) |

### 1.3 GeminiRunner

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.3.1 | `AgentName_ReturnsGemini` | Property check | "gemini" |
| 1.3.2 | `DefaultModel_Returns25Pro` | Default | "gemini-2.5-pro" |
| 1.3.3 | `AvailableModels_Contains25ProAnd25Flash` | Both | ["gemini-2.5-pro","gemini-2.5-flash"] |
| 1.3.4 | `BuildCommand_IncludesSandboxFalse` | Sandbox disabled | Contains `--sandbox=false` |
| 1.3.5 | `BuildCommand_PassesModelDirectly` | Model param | Contains `--model gemini-2.5-pro` |
| 1.3.6 | `BuildCommand_EscapesSingleQuotes` | Quote escaping | Properly escaped |
| 1.3.7 | `ParseResponse_AlwaysSuccess` | Any output | Success=true always |
| 1.3.8 | `ParseResponse_RawOutputPreserved` | Entire output | Output = rawOutput |
| 1.3.9 | `ParseResponse_NoCostOrTokens` | Any output | All zeros |

### 1.4 CliAgentFactory

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.4.1 | `Create_Claude_ReturnsClaudeRunner` | Create("claude") | ClaudeRunner instance |
| 1.4.2 | `Create_Codex_ReturnsCodexRunner` | Create("codex") | CodexRunner instance |
| 1.4.3 | `Create_Gemini_ReturnsGeminiRunner` | Create("gemini") | GeminiRunner instance |
| 1.4.4 | `Create_UnknownAgent_ThrowsArgumentException` | Create("unknown") | ArgumentException |
| 1.4.5 | `Create_CaseInsensitive` | Create("Claude") | Works or throws (test actual behavior) |
| 1.4.6 | `Create_EmptyString_Throws` | Create("") | ArgumentException |
| 1.4.7 | `Create_Null_Throws` | Create(null) | ArgumentNullException |
| 1.4.8 | `AvailableAgents_ContainsAll` | AvailableAgents | ["claude","codex","gemini"] |
| 1.4.9 | `AvailableAgents_OrderConsistent` | Multiple calls | Same order each time |

### 1.5 DockerContainerManager

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.5.1 | `SpawnAsync_CreatesAndStartsContainer` | Mock Docker client | Returns ContainerInfo with ID |
| 1.5.2 | `SpawnAsync_SetsMemoryLimit` | 4096MB config | Memory = 4096 * 1024 * 1024 |
| 1.5.3 | `SpawnAsync_SetsCpuLimit` | 2 CPU config | NanoCPUs = 2 * 1e9 |
| 1.5.4 | `SpawnAsync_BindsWorkspace` | workspace path | Binds contains path:/workspace |
| 1.5.5 | `SpawnAsync_MountsDockerSocket_WhenEnabled` | MountDockerSocket=true | Docker.sock bind present |
| 1.5.6 | `SpawnAsync_DoesNotMountDockerSocket_WhenDisabled` | MountDockerSocket=false | No docker.sock |
| 1.5.7 | `SpawnAsync_EnablesGui_WithPortBinding` | EnableGui=true, GuiPort=6080 | Port 6080 exposed, GuiUrl returned |
| 1.5.8 | `SpawnAsync_NoGui_NoPortBinding` | EnableGui=false | No exposed ports, GuiUrl=null |
| 1.5.9 | `SpawnAsync_SetsEnvironmentVariables` | Env dict with 3 vars | Env list has 3 KEY=VALUE entries |
| 1.5.10 | `SpawnAsync_SetsTtyAndStdin` | Always | Tty=true, OpenStdin=true |
| 1.5.11 | `SpawnAsync_CancellationToken_Honored` | Cancel before complete | OperationCanceledException |
| 1.5.12 | `ExecAsync_ReturnsCombinedOutput` | Command with stdout | Output captured |
| 1.5.13 | `ExecAsync_ReturnsExitCode` | Failed command | ExitCode != 0 |
| 1.5.14 | `ExecAsync_SetsWorkingDir` | workDir param | ExecParams.WorkingDir set |
| 1.5.15 | `ExecAsync_UsesBashShell` | Any command | Cmd = ["bash", "-c", command] |
| 1.5.16 | `ExecAsync_CancellationToken_Honored` | Cancel | OperationCanceledException |
| 1.5.17 | `ExecStreamingAsync_CallsOnOutputForEachChunk` | Multi-chunk output | onOutput called multiple times |
| 1.5.18 | `ExecStreamingAsync_AccumulatesStdout` | Stream output | Full output in return |
| 1.5.19 | `ExecStreamingAsync_SeparatesStdoutStderr` | Both streams | stdout in Output, stderr in Error |
| 1.5.20 | `ExecStreamingAsync_RespectsTimeout` | Timeout = 1s, long command | Cancels after timeout |
| 1.5.21 | `ExecStreamingAsync_ReturnsOnEOF` | Normal completion | Returns with full output |
| 1.5.22 | `ExecStreamingAsync_CancellationToken_Honored` | External cancel | Stops |
| 1.5.23 | `DestroyAsync_StopsAndRemovesContainer` | Valid container | Stop then Remove called |
| 1.5.24 | `DestroyAsync_AlreadyStopped_StillRemoves` | Container already stopped | Remove succeeds (stop swallowed) |
| 1.5.25 | `DestroyAsync_ForceRemove` | Any | Force=true |
| 1.5.26 | `DestroyAsync_CleansUpGuiUrl` | Container with GUI | GuiUrl removed from dict |
| 1.5.27 | `IsRunningAsync_RunningContainer_ReturnsTrue` | Running state | true |
| 1.5.28 | `IsRunningAsync_StoppedContainer_ReturnsFalse` | Stopped state | false |
| 1.5.29 | `IsRunningAsync_NonExistent_ReturnsFalse` | Bad container ID | false (exception caught) |
| 1.5.30 | `GetGuiUrl_WithGui_ReturnsUrl` | Container spawned with GUI | URL string |
| 1.5.31 | `GetGuiUrl_WithoutGui_ReturnsNull` | No GUI | null |
| 1.5.32 | `GetGuiUrl_UnknownContainer_ReturnsNull` | Bad ID | null |
| 1.5.33 | `Dispose_DisposesDockerClient` | Dispose called | No exceptions, client disposed |

### 1.6 SharedBlackboard

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.6.1 | `ClaimFile_Success` | Unclaimed file | Returns true |
| 1.6.2 | `ClaimFile_AlreadyClaimed_ReturnsFalse` | Claimed by other | Returns false |
| 1.6.3 | `ClaimFile_SameOwner_ReturnsFalse` | Double claim same owner | Returns false (already claimed) |
| 1.6.4 | `ReleaseFile_Success` | Owner releases | File released |
| 1.6.5 | `ReleaseFile_WrongOwner_Fails` | Non-owner releases | Returns false or throws |
| 1.6.6 | `ReleaseFile_UnclaimedFile` | Never claimed | No crash |
| 1.6.7 | `GetFileOwner_Claimed` | File with owner | Returns owner |
| 1.6.8 | `GetFileOwner_Unclaimed` | No owner | null |
| 1.6.9 | `SetTaskOutput_StoresValue` | Set then get | Matches |
| 1.6.10 | `SetTaskOutput_OverwritesExisting` | Set twice | Second value wins |
| 1.6.11 | `GetTaskOutput_MissingKey` | Non-existent | null |
| 1.6.12 | `Clear_RemovesEverything` | Claims + outputs | All gone |
| 1.6.13 | `ConcurrentClaims_ExactlyOneWins` | 100 threads claiming same file | Exactly 1 success |
| 1.6.14 | `ConcurrentClaims_DifferentFiles_AllSucceed` | 100 threads, different files | All succeed |
| 1.6.15 | `ClaimFile_EmptyPath` | "" file path | Handles gracefully |
| 1.6.16 | `ClaimFile_PathWithSpecialChars` | Path with spaces, unicode | Works correctly |

### 1.7 WorktreeManager

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.7.1 | `CreateWorktreeAsync_RunsGitCommand` | Branch "feat/x" | Runs `git worktree add` |
| 1.7.2 | `CreateWorktreeAsync_ReturnsPath` | Valid branch | Returns worktree path |
| 1.7.3 | `MergeWorktreeAsync_Success` | Clean merge | Success=true |
| 1.7.4 | `MergeWorktreeAsync_Conflict` | Conflicting changes | Returns conflict files list |
| 1.7.5 | `MergeWorktreeAsync_AbortsOnConflict` | Conflict detected | Runs `git merge --abort` |
| 1.7.6 | `CleanupWorktreeAsync_RemovesWorktree` | Valid path | Runs `git worktree remove` |
| 1.7.7 | `CleanupWorktreeAsync_DeletesBranch` | With branch name | Runs `git branch -D` |
| 1.7.8 | `CreateWorktreeAsync_InvalidBranch` | Illegal branch name | Fails gracefully |

### 1.8 LocalExecutionEnvironment

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.8.1 | `Kind_ReturnsLocal` | Property | "local" |
| 1.8.2 | `RunCommandAsync_CapturesOutput` | echo "hello" | "hello" |
| 1.8.3 | `RunCommandAsync_HandlesFailure` | exit 1 | Exception or error |
| 1.8.4 | `RunCommandAsync_RespectsWorkDir` | cd specific dir | Correct dir |
| 1.8.5 | `RunCommandAsync_WindowsVsUnix` | Platform detection | Uses cmd.exe on Windows, bash on Linux |
| 1.8.6 | `StartProcessAsync_ReturnsProcess` | Valid command | Process object |
| 1.8.7 | `RunCommandAsync_CancellationToken` | Cancel during exec | OperationCanceledException |

### 1.9 VerificationPipeline

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 1.9.1 | `RunAsync_AllGatesPass` | All pass | AllPassed=true |
| 1.9.2 | `RunAsync_BlockingGateFails_StopsEarly` | First blocking fails | Subsequent gates not called |
| 1.9.3 | `RunAsync_NonBlockingFails_Continues` | Non-blocking fails | Continues to next gate |
| 1.9.4 | `RunAsync_SkipsGatesThatCannotVerify` | CanVerify=false | Gate skipped |
| 1.9.5 | `RunAsync_EmptyFilter_ReturnsEmpty` | No gates in filter | No results |
| 1.9.6 | `RunAsync_GateFilterApplied` | Filter=["compile"] | Only compile gate runs |
| 1.9.7 | `RunAsync_PreservesIssuesList` | Gate returns issues | Issues in result |
| 1.9.8 | `RunAsync_MultipleBlockingGates_StopsOnFirst` | 2 blocking, first fails | Second not called |
| 1.9.9 | `RunAsync_NonBlockingThenBlocking_BothRun` | Non-blocking fails, then blocking | Both run |
| 1.9.10 | `RunAsync_AllPassed_TrueWhenNonBlockingFails` | Only non-blocking fails | AllPassed=true |
| 1.9.11 | `RunAsync_CancellationDuringGate` | Cancel mid-pipeline | OperationCanceledException |
| 1.9.12 | `RunAsync_GateThrowsException` | Gate throws | Propagates or captures |
| 1.9.13 | `RunAsync_OrderPreserved` | Multiple gates | Results in filter order |

---

## 2. Unit Tests — Verification Gates

### 2.1 CompileGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.1.1 | `Name_ReturnsCompile` | Property | "compile" |
| 2.1.2 | `IsBlocking_ReturnsTrue` | Property | true |
| 2.1.3 | `CanVerify_CsprojExists_ReturnsTrue` | .csproj in workspace | true |
| 2.1.4 | `CanVerify_PackageJsonExists_ReturnsTrue` | package.json present | true |
| 2.1.5 | `CanVerify_CargoTomlExists_ReturnsTrue` | Cargo.toml | true |
| 2.1.6 | `CanVerify_GoModExists_ReturnsTrue` | go.mod | true |
| 2.1.7 | `CanVerify_MakefileExists_ReturnsTrue` | Makefile | true |
| 2.1.8 | `CanVerify_NoProjectFiles_ReturnsFalse` | Empty dir | false |
| 2.1.9 | `Verify_DotnetBuildSuccess` | Exit code 0 | Passed=true |
| 2.1.10 | `Verify_DotnetBuildFailure` | Exit code 1, errors | Passed=false, issues extracted |
| 2.1.11 | `Verify_DetectsCorrectBuildSystem_Csproj` | .csproj | `dotnet build` |
| 2.1.12 | `Verify_DetectsCorrectBuildSystem_PackageJson` | package.json | `npm run build --if-present` |
| 2.1.13 | `Verify_DetectsCorrectBuildSystem_CargoToml` | Cargo.toml | `cargo build` |
| 2.1.14 | `Verify_DetectsCorrectBuildSystem_GoMod` | go.mod | `go build ./...` |
| 2.1.15 | `Verify_DetectsCorrectBuildSystem_Makefile` | Makefile | `make` |
| 2.1.16 | `Verify_ParsesBuildErrors` | "error CS1234" in output | Extracted |
| 2.1.17 | `Verify_Filters0ErrorLines` | "0 Error(s)" | Not included in issues |
| 2.1.18 | `Verify_MeasuresDuration` | Any run | Duration > 0 |
| 2.1.19 | `Verify_NoBuildSystem_ReturnsEcho` | No recognized files | echo message |

### 2.2 TestGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.2.1 | `Name_ReturnsTest` | Property | "test" |
| 2.2.2 | `IsBlocking_ReturnsTrue` | Property | true |
| 2.2.3 | `CanVerify_TestProjectExists` | *.Tests.csproj | true |
| 2.2.4 | `CanVerify_JestConfigExists` | jest.config.* | true |
| 2.2.5 | `CanVerify_PytestIniExists` | pytest.ini | true |
| 2.2.6 | `CanVerify_NoTestFramework` | Nothing | false |
| 2.2.7 | `Verify_DotnetTestPass` | All tests pass | Passed=true |
| 2.2.8 | `Verify_DotnetTestFail` | Tests fail | Passed=false, failures extracted |
| 2.2.9 | `Verify_NpmTestPass` | npm test succeeds | Passed=true |
| 2.2.10 | `Verify_NpmTestFail` | npm test fails | Passed=false |
| 2.2.11 | `Verify_PytestPass` | pytest succeeds | Passed=true |
| 2.2.12 | `Verify_PytestFail` | pytest fails | Passed=false |

### 2.3 CoverageGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.3.1 | `Name_ReturnsCoverage` | Property | "coverage" |
| 2.3.2 | `IsBlocking_ReturnsTrue` | Property | true |
| 2.3.3 | `Verify_AboveThreshold_Passes` | 80% coverage, 70% threshold | Passed=true |
| 2.3.4 | `Verify_BelowThreshold_Fails` | 50% coverage, 70% threshold | Passed=false |
| 2.3.5 | `Verify_ExactThreshold_Passes` | 70% coverage, 70% threshold | Passed=true |
| 2.3.6 | `Verify_CustomThreshold` | threshold=90, coverage=85 | Passed=false |
| 2.3.7 | `Verify_ZeroCoverage` | 0% | Passed=false |
| 2.3.8 | `Verify_100Percent` | 100% | Passed=true |

### 2.4 SecurityGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.4.1 | `Name_ReturnsSecurity` | Property | "security" |
| 2.4.2 | `IsBlocking_ReturnsFalse` | Property | false |
| 2.4.3 | `CanVerify_AlwaysTrue` | Any workspace | true |
| 2.4.4 | `Verify_CleanCode_NoIssues` | Clean source files | Passed=true |
| 2.4.5 | `Verify_HardcodedPassword` | `password = "secret"` | Detected |
| 2.4.6 | `Verify_HardcodedApiKey` | `api_key = "sk-xxx"` | Detected |
| 2.4.7 | `Verify_HardcodedSecret` | `secret = "abc123"` | Detected |
| 2.4.8 | `Verify_SecretKeyToken` | `sk-1234567890abcdef1234` | Detected |
| 2.4.9 | `Verify_EvalUsage` | `eval(userInput)` | Detected |
| 2.4.10 | `Verify_ExecInjection` | `exec("rm -rf")` | Detected |
| 2.4.11 | `Verify_InnerHTMLXss` | `.innerHTML = data` | Detected |
| 2.4.12 | `Verify_SqlInjection` | `SELECT * FROM + request` | Detected |
| 2.4.13 | `Verify_MultipleIssues` | Multiple patterns | All listed |
| 2.4.14 | `Verify_OneIssuePerLine` | Line matches 2 patterns | Only first reported |
| 2.4.15 | `Verify_IgnoresNodeModules` | Issue in node_modules | Not reported |
| 2.4.16 | `Verify_IgnoresBinObj` | Issue in bin/obj | Not reported |
| 2.4.17 | `Verify_TruncatesLongLines` | 500-char match | Truncated to 200 |

### 2.5 LintGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.5.1 | `Name_ReturnsLint` | Property | "lint" |
| 2.5.2 | `IsBlocking_ReturnsFalse` | Property | false |
| 2.5.3 | `CanVerify_DetectsDotnetFormat` | .csproj | true |
| 2.5.4 | `CanVerify_DetectsEslint` | .eslintrc or eslint config | true |
| 2.5.5 | `Verify_CleanCode` | No lint errors | Passed=true |
| 2.5.6 | `Verify_LintErrors` | Warnings present | Issues extracted |

### 2.6 HallucinationDetector

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.6.1 | `Name_ReturnsHallucination` | Property | "hallucination" |
| 2.6.2 | `IsBlocking_ReturnsTrue` | Property | true |
| 2.6.3 | `Verify_AllImportsExist` | Valid imports | Passed=true |
| 2.6.4 | `Verify_PhantomFileImport` | Import of non-existent file | Passed=false, file listed |
| 2.6.5 | `Verify_IgnoresStdLibImports` | `import os` in Python | Ignored |
| 2.6.6 | `Verify_IgnoresNodeModuleImports` | `require('express')` | Ignored |
| 2.6.7 | `Verify_MultiplePhantomFiles` | 3 bad imports | All 3 listed |
| 2.6.8 | `Verify_RelativeImportCheck` | `./utils/missing.js` | Detected |

### 2.7 QualityReviewGate

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 2.7.1 | `Name_ReturnsQuality` | Property | "quality" |
| 2.7.2 | `IsBlocking_ReturnsFalse` | Property | false |
| 2.7.3 | `Verify_DetectsTodo` | `// TODO fix this` | Reported |
| 2.7.4 | `Verify_DetectsFixme` | `// FIXME` | Reported |
| 2.7.5 | `Verify_DetectsHack` | `// HACK` | Reported |
| 2.7.6 | `Verify_DetectsConsoleWrite` | `Console.WriteLine` | Reported |
| 2.7.7 | `Verify_DetectsThreadSleep` | `Thread.Sleep(1000)` | Reported |
| 2.7.8 | `Verify_DetectsBlockingResult` | `.Result` or `.Wait()` | Reported |
| 2.7.9 | `Verify_DetectsEmptyCatch` | `catch { }` | Reported |
| 2.7.10 | `Verify_DetectsDateTimeNow` | `DateTime.Now` | Reported |
| 2.7.11 | `Verify_CleanCode_Passes` | No quality issues | Passed=true |

---

## 3. Unit Tests — Activities

### 3.1 RunCliAgentActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.1.1 | `Execute_CallsAgentFactory` | Agent="claude" | Factory.Create("claude") called |
| 3.1.2 | `Execute_BuildsCommand` | Prompt set | BuildCommand called with correct params |
| 3.1.3 | `Execute_UsesStreamingExec` | Any | ExecStreamingAsync called (NOT ExecAsync) |
| 3.1.4 | `Execute_StreamsOutputViaLogEntries` | Chunks arrive | AddExecutionLogEntry("OutputChunk") per chunk |
| 3.1.5 | `Execute_ParsesResponse` | Output received | ParseResponse called |
| 3.1.6 | `Execute_SetsAllOutputs` | Success response | Response, Success, CostUsd, FilesModified, ExitCode all set |
| 3.1.7 | `Execute_DoneOutcome_OnSuccess` | Success=true | CompleteWithOutcomes("Done") |
| 3.1.8 | `Execute_FailedOutcome_OnFailure` | Success=false | CompleteWithOutcomes("Failed") |
| 3.1.9 | `Execute_DefaultValues` | No overrides | Agent=claude, Model=auto, MaxTurns=20, Timeout=30 |
| 3.1.10 | `Execute_TimeoutApplied` | Timeout=5 | TimeSpan.FromMinutes(5) |
| 3.1.11 | `Execute_CancellationPropagated` | Token cancelled | Stops execution |
| 3.1.12 | `Execute_CodexAgent` | Agent="codex" | CodexRunner used, correct flags |
| 3.1.13 | `Execute_GeminiAgent` | Agent="gemini" | GeminiRunner used, correct flags |

### 3.2 TriageActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.2.1 | `Execute_UsesHaikuModel` | Always | BuildCommand with "haiku" |
| 3.2.2 | `Execute_BuildsTriagePrompt` | User prompt | Contains "complexity" and user text |
| 3.2.3 | `Execute_SimpleTask_SimpleOutcome` | Complexity < 7 | Outcome="Simple" |
| 3.2.4 | `Execute_ComplexTask_ComplexOutcome` | Complexity >= 7 | Outcome="Complex" |
| 3.2.5 | `Execute_Threshold7_IsComplex` | Complexity = 7 | Outcome="Complex" |
| 3.2.6 | `Execute_Threshold6_IsSimple` | Complexity = 6 | Outcome="Simple" |
| 3.2.7 | `Execute_SetsComplexityOutput` | Valid response | Complexity output set |
| 3.2.8 | `Execute_SetsCategoryOutput` | Valid response | Category output set |
| 3.2.9 | `Execute_SetsRecommendedModelOutput` | Valid response | RecommendedModel output set |
| 3.2.10 | `Execute_MalformedJson_DefaultValues` | Non-JSON output | Complexity=5, Category="code_gen", Model="sonnet" |
| 3.2.11 | `Execute_PartialJson_ExtractsAvailable` | Missing some fields | Available fields extracted, defaults for rest |
| 3.2.12 | `Execute_EmitsLogEntry` | Any run | Log entry with Complexity and Category |
| 3.2.13 | `Execute_UsesExecAsync_NotStreaming` | Always | ExecAsync called (triage doesn't need streaming) |

### 3.3 ArchitectActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.3.1 | `Execute_UsesOpusModel` | Always | BuildCommand with "opus" |
| 3.3.2 | `Execute_IncludesGapContext` | GapContext provided | Prompt contains gap context |
| 3.3.3 | `Execute_NoGapContext` | GapContext null/empty | Prompt without gap section |
| 3.3.4 | `Execute_ParsesTaskArray` | Valid JSON array | Tasks extracted |
| 3.3.5 | `Execute_ExtractsArrayFromMixedOutput` | Text before/after JSON | Array found via indexOf |
| 3.3.6 | `Execute_SetsTaskCount` | 3 tasks | TaskCount=3 |
| 3.3.7 | `Execute_DoneOutcome_WithTasks` | Non-empty tasks | Outcome="Done" |
| 3.3.8 | `Execute_FailedOutcome_NoTasks` | Empty tasks | Outcome="Failed" |
| 3.3.9 | `Execute_MalformedJson_EmptyTasks` | Invalid JSON | Tasks=[], Outcome="Failed" |
| 3.3.10 | `Execute_EmitsLogEntry` | Any | Log with task count |

### 3.4 ModelRouterActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.4.1 | `Execute_Claude_Complexity1_Haiku` | claude, 1 | haiku |
| 3.4.2 | `Execute_Claude_Complexity3_Haiku` | claude, 3 | haiku |
| 3.4.3 | `Execute_Claude_Complexity4_Sonnet` | claude, 4 | sonnet |
| 3.4.4 | `Execute_Claude_Complexity7_Sonnet` | claude, 7 | sonnet |
| 3.4.5 | `Execute_Claude_Complexity8_Opus` | claude, 8 | opus |
| 3.4.6 | `Execute_Claude_Complexity10_Opus` | claude, 10 | opus |
| 3.4.7 | `Execute_Codex_Complexity5_O4Mini` | codex, 5 | o4-mini |
| 3.4.8 | `Execute_Codex_Complexity6_O3` | codex, 6 | o3 |
| 3.4.9 | `Execute_Gemini_AnyComplexity_25Pro` | gemini, any | gemini-2.5-pro |
| 3.4.10 | `Execute_UnknownAgent_DefaultsSonnet` | "foo", any | sonnet |
| 3.4.11 | `Execute_AlwaysDoneOutcome` | Any | "Done" |
| 3.4.12 | `Execute_EmitsLogEntry` | Any | Log with agent/model/complexity |

### 3.5 SpawnContainerActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.5.1 | `Execute_CallsSpawnAsync` | Valid config | ContainerManager.SpawnAsync called |
| 3.5.2 | `Execute_SetsContainerIdOutput` | Spawn succeeds | ContainerId output set |
| 3.5.3 | `Execute_SetsGuiUrlOutput` | GUI enabled | GuiUrl output set |
| 3.5.4 | `Execute_DoneOutcome_OnSuccess` | Spawn succeeds | "Done" |
| 3.5.5 | `Execute_FailedOutcome_OnException` | Spawn throws | "Failed" |
| 3.5.6 | `Execute_DefaultMemory4096` | No override | 4096 MB |
| 3.5.7 | `Execute_EmitsContainerSpawnedLog` | Any | Log entry emitted |

### 3.6 ExecInContainerActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.6.1 | `Execute_RunsCommand` | Command set | ExecAsync called |
| 3.6.2 | `Execute_SetsOutputAndExitCode` | Command result | Both outputs set |
| 3.6.3 | `Execute_DoneOnZeroExit` | ExitCode=0 | "Done" |
| 3.6.4 | `Execute_FailedOnNonZeroExit` | ExitCode=1 | "Failed" |

### 3.7 StreamFromContainerActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.7.1 | `Execute_UsesStreamingExec` | Any | ExecStreamingAsync called |
| 3.7.2 | `Execute_EmitsLogEntriesPerChunk` | Multi-chunk | Multiple log entries |
| 3.7.3 | `Execute_SetsFullOutput` | Complete stream | Full concatenated output |
| 3.7.4 | `Execute_RespectsTimeout` | Timeout input | Passed to streaming |

### 3.8 DestroyContainerActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.8.1 | `Execute_CallsDestroyAsync` | ContainerId | Destroy called |
| 3.8.2 | `Execute_DoneOnSuccess` | Destroy succeeds | "Done" |
| 3.8.3 | `Execute_FailedOnException` | Destroy throws | "Failed" |
| 3.8.4 | `Execute_EmitsContainerDestroyedLog` | Any | Log entry |

### 3.9 RunVerificationActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.9.1 | `Execute_RunsPipeline` | Gates selected | Pipeline.RunAsync called |
| 3.9.2 | `Execute_PassedOutcome` | AllPassed=true | "Passed" |
| 3.9.3 | `Execute_FailedOutcome` | AllPassed=false | "Failed" |
| 3.9.4 | `Execute_SetsFailedGatesOutput` | Some fail | Failed gate names |
| 3.9.5 | `Execute_SetsGateResultsJson` | Any | JSON serialized results |
| 3.9.6 | `Execute_EmitsVerificationLog` | Any | Log entry |

### 3.10 RepairActivity

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.10.1 | `Execute_BuildsRepairPrompt` | Failed gates | Contains gate names and results |
| 3.10.2 | `Execute_IncludesOriginalPrompt` | Prompt set | In repair prompt |
| 3.10.3 | `Execute_IncludesFailedGatesList` | 2 gates fail | Both listed |
| 3.10.4 | `Execute_IncludesInstructions` | Any | "Fix ONLY the issues" in prompt |
| 3.10.5 | `Execute_EmitsLog` | Any | Log entry |

### 3.11 Git Activities (CreateWorktree, MergeWorktree, CleanupWorktree)

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.11.1 | `CreateWorktree_SetsWorktreePathOutput` | Success | Path set |
| 3.11.2 | `CreateWorktree_FailedOnError` | Git fails | "Failed" outcome |
| 3.11.3 | `MergeWorktree_SuccessOutput` | Clean merge | Success=true |
| 3.11.4 | `MergeWorktree_ConflictOutput` | Merge conflict | ConflictFiles set |
| 3.11.5 | `CleanupWorktree_DoneOnSuccess` | Cleanup works | "Done" |
| 3.11.6 | `CleanupWorktree_FailedOnError` | Cleanup fails | "Failed" |

### 3.12 Infrastructure Activities

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 3.12.1 | `EmitOutputChunk_EmitsLogEntry` | Text="hello" | Log with text |
| 3.12.2 | `EmitOutputChunk_IncludesSource` | Source="agent" | In log |
| 3.12.3 | `EmitOutputChunk_IncludesLevel` | Level="error" | In log |
| 3.12.4 | `ClaimFile_Claimed_ClaimedOutcome` | Unclaimed file | "Claimed" |
| 3.12.5 | `ClaimFile_AlreadyClaimed_AlreadyClaimedOutcome` | Claimed | "AlreadyClaimed" |
| 3.12.6 | `ClaimFile_SetsCurrentOwnerOutput` | Claimed by other | Owner ID |
| 3.12.7 | `HumanApproval_CreatesBookmark` | Execute | Bookmark created |
| 3.12.8 | `UpdateCost_EmitsLogEntry` | Cost data | Log entry |
| 3.12.9 | `UpdateCost_AccumulatesTotal` | Multiple calls | Summed |

---

## 4. Unit Tests — Models & DTOs

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 4.1 | `CliAgentResponse_RecordEquality` | Two identical records | Equal |
| 4.2 | `CliAgentResponse_DefaultValues` | Empty construction | Sensible defaults |
| 4.3 | `GateResult_RecordEquality` | Two identical records | Equal |
| 4.4 | `TriageResult_RecordEquality` | Two identical | Equal |
| 4.5 | `ContainerConfig_DefaultValues` | New instance | Memory=4096, Cpu=2, WorkDir="/workspace" |
| 4.6 | `ContainerConfig_EnvDictionaryEmpty` | New instance | Empty dict, not null |
| 4.7 | `ContainerInfo_RecordEquality` | Two identical | Equal |
| 4.8 | `ExecResult_RecordEquality` | Two identical | Equal |
| 4.9 | `PipelineResult_AllPassed_True` | All gates pass | true |
| 4.10 | `PipelineResult_AllPassed_False` | Blocking gate fails | false |
| 4.11 | `SessionInfo_DefaultState` | New instance | "idle" |
| 4.12 | `SessionInfo_CreatedAtIsUtc` | New instance | Kind = Utc |
| 4.13 | `MagicPaiConfig_AllDefaults` | New instance | All defaults match expected |
| 4.14 | `MagicPaiConfig_DefaultGates` | New instance | ["compile","test","hallucination"] |

---

## 5. Unit Tests — Server Components

### 5.1 SessionHub

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.1.1 | `CreateSession_DispatchesWorkflow` | Valid params | WorkflowDispatcher called |
| 5.1.2 | `CreateSession_RegistersWithTracker` | Create | Tracker.RegisterSession called |
| 5.1.3 | `CreateSession_ReturnsSessionId` | Create | Non-empty ID |
| 5.1.4 | `CreateSession_AddsToSignalRGroup` | Create | Groups.Add called |
| 5.1.5 | `CreateSession_TracksConnection` | Create | ConnectionSessions updated |
| 5.1.6 | `CreateSession_TruncatesLongPrompt` | 200-char prompt | Preview is 103 chars |
| 5.1.7 | `StopSession_CancelsWorkflow` | Valid session | CancellationDispatcher called |
| 5.1.8 | `StopSession_UpdatesState` | Cancel | State = "cancelled" |
| 5.1.9 | `StopSession_BroadcastsStateChange` | Cancel | SignalR "sessionStateChanged" |
| 5.1.10 | `Approve_DispatchesResume` | Approved | WorkflowDispatcher called |
| 5.1.11 | `Approve_BroadcastsApproval` | Any | "approvalProcessed" event |
| 5.1.12 | `GetSessionOutput_ReturnsBuffered` | Output exists | String array |
| 5.1.13 | `JoinSession_AddsToGroup` | Session ID | Groups.Add called |
| 5.1.14 | `LeaveSession_RemovesFromGroup` | Session ID | Groups.Remove called |
| 5.1.15 | `GetSession_Found` | Valid ID | SessionInfo |
| 5.1.16 | `GetSession_NotFound` | Invalid ID | null |
| 5.1.17 | `ListSessions_ReturnsAll` | Multiple sessions | All returned |
| 5.1.18 | `OnDisconnected_CleansUpGroups` | Disconnect | Groups cleaned |
| 5.1.19 | `OnDisconnected_CleansUpConnectionMap` | Disconnect | Removed from dict |

### 5.2 SessionController (REST API)

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.2.1 | `POST_CreateSession_Returns200` | Valid request | 200 + CreateSessionResponse |
| 5.2.2 | `POST_CreateSession_DefaultValues` | Minimal request | Agent=claude, Model=sonnet |
| 5.2.3 | `POST_CreateSession_CustomWorkflow` | workflowName="simple" | Dispatched correctly |
| 5.2.4 | `GET_ListSessions_ReturnsArray` | Sessions exist | 200 + array |
| 5.2.5 | `GET_ListSessions_Empty` | No sessions | 200 + [] |
| 5.2.6 | `GET_GetSession_Found` | Valid ID | 200 + SessionInfo |
| 5.2.7 | `GET_GetSession_NotFound` | Invalid ID | 404 |
| 5.2.8 | `DELETE_StopSession_Success` | Running session | 200 + message |
| 5.2.9 | `DELETE_StopSession_NotFound` | Invalid ID | 404 |
| 5.2.10 | `POST_Approve_Success` | Valid ID + body | 200 |
| 5.2.11 | `POST_Approve_NotFound` | Invalid ID | 404 |
| 5.2.12 | `GET_GetOutput_Success` | Valid ID | 200 + string[] |
| 5.2.13 | `GET_GetOutput_NotFound` | Invalid ID | 404 |
| 5.2.14 | `POST_CreateSession_MissingPrompt` | No prompt | 400 |

### 5.3 ElsaEventBridge

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.3.1 | `Handle_OutputChunk_ForwardsToSignalR` | OutputChunk event | outputChunk sent to group |
| 5.3.2 | `Handle_OutputChunk_AppendsToTracker` | OutputChunk | AppendOutput called |
| 5.3.3 | `Handle_OutputChunk_ParsesJsonPayload` | JSON text payload | Text extracted |
| 5.3.4 | `Handle_OutputChunk_FallbackOnBadJson` | Non-JSON payload | Raw message sent |
| 5.3.5 | `Handle_VerificationComplete` | Event | verificationUpdate sent |
| 5.3.6 | `Handle_ContainerSpawned` | Event | containerEvent sent |
| 5.3.7 | `Handle_ContainerDestroyed` | Event | containerEvent sent |
| 5.3.8 | `Handle_TriageResult` | Event | taskEvent sent |
| 5.3.9 | `Handle_ArchitectResult` | Event | taskEvent sent |
| 5.3.10 | `Handle_RepairPromptGenerated` | Event | taskEvent sent |
| 5.3.11 | `Handle_UnknownEvent_Ignored` | "FooBar" event | No SignalR message |
| 5.3.12 | `Handle_NullPayload_Skipped` | null payload | No crash |

### 5.4 SessionTracker

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.4.1 | `RegisterSession_Stores` | Register | GetSession returns it |
| 5.4.2 | `UpdateState_Changes` | running -> completed | State updated |
| 5.4.3 | `AppendOutput_StoresText` | Append "hello" | GetOutput contains "hello" |
| 5.4.4 | `AppendOutput_CircularBuffer` | 1001 entries | Max 1000 retained |
| 5.4.5 | `GetOutput_EmptySession` | No output | Empty array |
| 5.4.6 | `GetOutput_NonExistentSession` | Bad ID | Empty array |
| 5.4.7 | `GetSession_NonExistent` | Bad ID | null |
| 5.4.8 | `GetAllSessions_MultipleReturned` | 3 sessions | 3 items |
| 5.4.9 | `RemoveSession_Removes` | Remove | GetSession=null |
| 5.4.10 | `ConcurrentAppendOutput_ThreadSafe` | 100 threads | No data loss/corruption |
| 5.4.11 | `UpdateState_NonExistentSession` | Bad ID | No crash |
| 5.4.12 | `AppendOutput_LargeText` | 1MB string per entry | Handled |
| 5.4.13 | `RegisterSession_DuplicateId` | Same ID twice | Overwrites or rejects |
| 5.4.14 | `Constructor_DefaultBufferSize1000` | Default | Max 1000 entries |
| 5.4.15 | `Constructor_CustomBufferSize` | maxBufferSize=10 | Max 10 entries |
| 5.4.16 | `AppendOutput_UnregisteredSession_CreatesBuffer` | No prior register | Buffer created on-demand |
| 5.4.17 | `AppendOutput_Trim_RemovesOldest` | Exceed buffer | First entry dequeued |

### 5.5 WorkflowProgressTracker

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.5.1 | `Handle_ActivityCompleted_SendsProgress` | CompletedAt set | workflowProgress sent with status="completed" |
| 5.5.2 | `Handle_ActivityRunning_SendsProgress` | CompletedAt null | workflowProgress sent with status="running" |
| 5.5.3 | `Handle_FaultedActivity_UpdatesStateFailed` | Status=Faulted | Tracker state set to "failed" |
| 5.5.4 | `Handle_FaultedActivity_BroadcastsStateChange` | Status=Faulted | sessionStateChanged "failed" sent |
| 5.5.5 | `Handle_UnknownSession_Skips` | Session not in tracker | No SignalR messages |
| 5.5.6 | `Handle_ActivityNameIncluded` | Named activity | ActivityName in event |
| 5.5.7 | `Handle_ActivityTypeIncluded` | Type only (no name) | ActivityType in event |
| 5.5.8 | `Handle_MultipleRecords_AllProcessed` | 5 activity updates | 5 progress events |

### 5.6 DI Registration & Server Startup

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 5.6.1 | `DI_SharedBlackboard_IsSingleton` | Resolve twice | Same instance |
| 5.6.2 | `DI_DockerContainerManager_Registered` | Resolve IContainerManager | DockerContainerManager |
| 5.6.3 | `DI_CliAgentFactory_Registered` | Resolve ICliAgentFactory | CliAgentFactory |
| 5.6.4 | `DI_AllGates_Registered` | Resolve IEnumerable<IVerificationGate> | 7 gates |
| 5.6.5 | `DI_VerificationPipeline_Registered` | Resolve | VerificationPipeline |
| 5.6.6 | `DI_SessionTracker_IsSingleton` | Resolve twice | Same instance |
| 5.6.7 | `DI_MagicPaiConfig_LoadsFromAppSettings` | Resolve | Non-null, correct values |
| 5.6.8 | `DI_SignalR_Configured` | Hub mapping | /hub mapped |
| 5.6.9 | `DI_Controllers_Mapped` | Session API | Endpoints respond |
| 5.6.10 | `DI_Elsa_AllWorkflows_Registered` | Runtime config | 17 workflows registered |
| 5.6.11 | `DI_Elsa_CustomActivities_Registered` | Activity registry | All MagicPAI activities found |
| 5.6.12 | `DI_ElsaEventBridge_Registered` | Notification handler | Registered |
| 5.6.13 | `DI_WorkflowProgressTracker_Registered` | Notification handler | Registered |
| 5.6.14 | `CORS_AllowsAnyOrigin` | Cross-origin request | Not blocked |
| 5.6.15 | `Elsa_Identity_SigningKey_Set` | Config | Key present |
| 5.6.16 | `Elsa_SQLite_Persistence` | DB mode | SQLite used for dev |
| 5.6.17 | `Server_FallbackToIndexHtml` | Unknown route | index.html served |

---

## 6. Unit Tests — Workflows

### 6.1 FullOrchestrateWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.1.1 | `Build_CreatesFlowchart` | Build workflow | Non-null flowchart |
| 6.1.2 | `Build_HasSpawnContainer` | Inspect activities | SpawnContainer present |
| 6.1.3 | `Build_HasTriage` | Inspect | Triage present |
| 6.1.4 | `Build_HasRunCliAgent` | Inspect | RunCliAgent present |
| 6.1.5 | `Build_HasVerification` | Inspect | RunVerification present |
| 6.1.6 | `Build_HasRepair` | Inspect | Repair present |
| 6.1.7 | `Build_HasDestroyContainer` | Inspect | Destroy present |
| 6.1.8 | `Build_SimplePathConnected` | Triage Simple -> RunAgent | Connected |
| 6.1.9 | `Build_ComplexPathConnected` | Triage Complex -> Architect | Connected |

### 6.2 SimpleAgentWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.2.1 | `Build_LinearFlow` | Spawn -> Agent -> Verify -> Destroy | All connected linearly |
| 6.2.2 | `Build_HasAllActivities` | All 4 | Present |

### 6.3 VerifyAndRepairWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.3.1 | `Build_HasRepairLoop` | While loop | Present |
| 6.3.2 | `Build_HasMaxAttempts` | Loop condition | Max attempts checked |
| 6.3.3 | `Build_ConnectsRepairToAgent` | Repair -> Agent -> Verify | Connected |

### 6.4 PromptEnhancerWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.4.1 | `Build_CreatesFlowchart` | Build | Non-null flowchart |
| 6.4.2 | `Build_StartsWithTriageClassifier` | Start activity | classify-vagueness (TriageActivity) |
| 6.4.3 | `Build_BothPathsLeadToSonnet` | Simple + Complex | Both connect to enhance-sonnet |
| 6.4.4 | `Build_QualityCheckAfterEnhance` | enhance-sonnet -> quality-check | Connected |
| 6.4.5 | `Build_EscalatesToOpus_OnComplexQuality` | quality-check Complex -> enhance-opus | Connected |
| 6.4.6 | `Build_SonnetFailed_FallsToOpus` | enhance-sonnet Failed -> enhance-opus | Connected |
| 6.4.7 | `Build_SimpleQualityCheck_Terminal` | quality-check Simple | No further connections (done) |

### 6.5 ContextGathererWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.5.1 | `Build_HasThreeParallelBranches` | Activities | research, repo-map, memory agents |
| 6.5.2 | `Build_AllBranchesConvergeToMerge` | Connections | 6 connections (Done+Failed for each) to merge |
| 6.5.3 | `Build_UsesHaikuForResearch` | research-context model | haiku |
| 6.5.4 | `Build_UsesSonnetForMerge` | merge-context model | sonnet |
| 6.5.5 | `Build_FailedBranches_StillMerge` | Each branch Failed | All connect to merge |

### 6.6 PromptGroundingWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.6.1 | `Build_TwoStepFlow` | analyze -> rewrite | Connected |
| 6.6.2 | `Build_AnalysisUsesHaiku` | analyze-codebase model | haiku |
| 6.6.3 | `Build_RewriteUsesSonnet` | rewrite-prompt model | sonnet |
| 6.6.4 | `Build_AnalysisFailed_StillRewrites` | analyze Failed -> rewrite | Connected |

### 6.7 LoopVerifierWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.7.1 | `Build_HasLoopBack` | classify Complex -> runner | Loop connection exists |
| 6.7.2 | `Build_SimpleExits` | classify Simple | Terminal (no further connection) |
| 6.7.3 | `Build_RunnerFailedGoesToClassify` | runner Failed -> classify | Connected |
| 6.7.4 | `Build_RunnerDoneGoesToClassify` | runner Done -> classify | Connected |

### 6.8 WebsiteAuditLoopWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.8.1 | `Build_HasFourPhases` | Activities | 7 activities (4 runners + 3 checks) |
| 6.8.2 | `Build_Phase1_DiscoveryLoop` | discovery-check Complex -> discovery-runner | Loop |
| 6.8.3 | `Build_Phase1_ToPhase2` | discovery-check Simple -> visual-runner | Connected |
| 6.8.4 | `Build_Phase2_VisualLoop` | visual-check Complex -> visual-runner | Loop |
| 6.8.5 | `Build_Phase2_ToPhase3` | visual-check Simple -> interaction-runner | Connected |
| 6.8.6 | `Build_Phase3_InteractionLoop` | interaction-check Complex -> interaction-runner | Loop |
| 6.8.7 | `Build_Phase3_ToPhase4` | interaction-check Simple -> opus-sweep | Connected |
| 6.8.8 | `Build_Phase1UsesHaiku` | discovery-runner model | haiku |
| 6.8.9 | `Build_Phase4UsesOpus` | opus-sweep model | opus |

### 6.9 IsComplexAppWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.9.1 | `Build_SingleTriageActivity` | Activities | Just 1 TriageActivity |
| 6.9.2 | `Build_NoConnections` | Connections | Empty (terminal classifier) |
| 6.9.3 | `Build_OutputsSimpleOrComplex` | TriageActivity outcomes | Simple, Complex |

### 6.10 IsWebsiteProjectWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.10.1 | `Build_SingleTriageActivity` | Activities | Just 1 TriageActivity |
| 6.10.2 | `Build_ClassifiesWebsitePresence` | Purpose | Detects website project |

### 6.11 OrchestrateComplexPathWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.11.1 | `Build_StartsWithArchitect` | Start | architect-decompose |
| 6.11.2 | `Build_ArchitectToModelRouter` | Done connection | architect -> model-router |
| 6.11.3 | `Build_ModelRouterToWorker` | Done connection | model-router -> complex-worker |
| 6.11.4 | `Build_WorkerToVerify` | Done connection | worker -> verify |
| 6.11.5 | `Build_VerifyFailedToRepair` | Failed connection | verify -> repair |
| 6.11.6 | `Build_RepairLoopBack` | repair -> repairAgent -> verify | Loop connected |
| 6.11.7 | `Build_VerifyPassedToMerge` | Passed connection | verify -> merge |
| 6.11.8 | `Build_WorkerFailed_MergesPartial` | worker Failed -> merge | Connected |
| 6.11.9 | `Build_RepairAgentFailed_Merges` | repairAgent Failed -> merge | Connected |
| 6.11.10 | `Build_ArchitectFailed_Terminal` | architect Failed | No connection (terminal) |

### 6.12 OrchestrateSimplePathWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.12.1 | `Build_ThreeStepFlow` | assemble -> execute -> verify | Connected |
| 6.12.2 | `Build_AssembleUsesHaiku` | assemble-prompt model | haiku |
| 6.12.3 | `Build_AssembleFailed_StillExecutes` | assemble Failed -> execute | Connected |
| 6.12.4 | `Build_ExecuteFailed_Terminal` | execute Failed | No further connection |

### 6.13 PostExecutionPipelineWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.13.1 | `Build_StartsWithCompletenessAudit` | Start | completeness-audit |
| 6.13.2 | `Build_AuditToReview` | Done/Failed connections | audit -> review-agent |
| 6.13.3 | `Build_ReviewLoopBack` | review-check Complex -> review-agent | Loop |
| 6.13.4 | `Build_ReviewOk_ToQualityGates` | review-check Simple -> quality-gates | Connected |
| 6.13.5 | `Build_QualityGatesPassed_ToE2E` | Passed -> e2e-test | Connected |
| 6.13.6 | `Build_QualityGatesFailed_ToRepair` | Failed -> repair | Connected |
| 6.13.7 | `Build_E2E_ToFinalVerify` | e2e Done/Failed -> final-verify | Connected |
| 6.13.8 | `Build_FinalVerifyFailed_RepairLoop` | Failed -> repair -> repairAgent -> finalVerify | Loop |

### 6.14 ResearchPipelineWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.14.1 | `Build_FourStepFlow` | enhance -> research -> triage -> route | Connected |
| 6.14.2 | `Build_EnhanceUsesSonnet` | research-enhance model | sonnet |
| 6.14.3 | `Build_ResearchUsesHaiku` | research-gather model | haiku |
| 6.14.4 | `Build_SimpleRouteToAgent` | triage Simple -> simpleAgent | Connected |
| 6.14.5 | `Build_ComplexRouteToArchitect` | triage Complex -> architect | Connected |
| 6.14.6 | `Build_ArchitectToComplexAgent` | architect Done -> complexAgent | Connected |

### 6.15 StandardOrchestrateWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.15.1 | `Build_StartsWithSpawn` | Start | std-spawn |
| 6.15.2 | `Build_SpawnToEnhance` | Done connection | spawn -> enhance |
| 6.15.3 | `Build_SpawnFailed_Destroys` | Failed connection | spawn -> destroy |
| 6.15.4 | `Build_EnhanceToElaborate` | Done/Failed | enhance -> elaborate |
| 6.15.5 | `Build_ElaborateToContext` | Done/Failed | elaborate -> context |
| 6.15.6 | `Build_ContextToTriage` | Done/Failed | context -> triage |
| 6.15.7 | `Build_TriageSimpleToAgent` | Simple | triage -> simpleAgent |
| 6.15.8 | `Build_TriageComplexToArchitect` | Complex | triage -> architect |
| 6.15.9 | `Build_SimpleAgentToVerify` | Done | simpleAgent -> simpleVerify |
| 6.15.10 | `Build_SimpleAgentFailed_Destroys` | Failed | simpleAgent -> destroy |
| 6.15.11 | `Build_SimpleVerifyPassed_Destroys` | Passed | simpleVerify -> destroy |
| 6.15.12 | `Build_ComplexRepairLoop` | verify Failed -> repair -> repairAgent -> verify | Loop |
| 6.15.13 | `Build_HasPromptEnhancementPhase` | 4 pre-execution steps | enhance, elaborate, context, triage |
| 6.15.14 | `Build_RepairAgentFailed_Destroys` | Failed | repairAgent -> destroy |

### 6.16 TestSetPromptWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.16.1 | `Build_SingleActivity` | Activities | 1 RunCliAgentActivity |
| 6.16.2 | `Build_UsesHaiku` | model | haiku |
| 6.16.3 | `Build_MinimalFlowchart` | No connections | Just start activity |

### 6.17 ClawEvalAgentWorkflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 6.17.1 | `Build_StartsWithTriage` | Start | eval-triage |
| 6.17.2 | `Build_BothPaths_ToContext` | Simple + Complex | Both -> gatherContext |
| 6.17.3 | `Build_ContextToExecution` | Done/Failed | gatherContext -> simpleExec |
| 6.17.4 | `Build_UsesHaikuForContext` | eval-context model | haiku |

---

## 7. Integration Tests — Docker

> **Prerequisite**: Docker Desktop running on test machine.

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 7.1 | `SpawnContainer_RealDocker_ReturnsId` | Spawn with alpine | Real container ID |
| 7.2 | `SpawnContainer_WorkspaceBindMounted` | Mount local dir | Files visible inside |
| 7.3 | `SpawnContainer_MemoryLimitEnforced` | 512MB limit | Container respects limit |
| 7.4 | `SpawnContainer_CpuLimitEnforced` | 1 CPU | Container limited |
| 7.5 | `SpawnContainer_EnvVarsAvailable` | Set FOO=bar | `echo $FOO` returns "bar" |
| 7.6 | `SpawnContainer_GuiPort_Exposed` | EnableGui=true | Port accessible |
| 7.7 | `ExecAsync_RealCommand_ReturnsOutput` | `echo hello` | "hello\n" |
| 7.8 | `ExecAsync_FailedCommand_NonZeroExit` | `exit 42` | ExitCode=42 |
| 7.9 | `ExecAsync_StderrCaptured` | `echo err >&2` | Error contains "err" |
| 7.10 | `ExecAsync_LongRunningCommand` | `sleep 5 && echo done` | Completes |
| 7.11 | `ExecStreamingAsync_StreamsInRealTime` | `for i in 1 2 3; do echo $i; sleep 1; done` | 3 callbacks over ~3s |
| 7.12 | `ExecStreamingAsync_LargeOutput` | `seq 1 100000` | All lines captured |
| 7.13 | `ExecStreamingAsync_Timeout_Fires` | Timeout=2s, `sleep 60` | Cancelled after 2s |
| 7.14 | `DestroyContainer_RemovesContainer` | Valid container | `docker ps -a` doesn't show it |
| 7.15 | `DestroyContainer_DoubleDestroy_NoError` | Destroy twice | Second call doesn't throw |
| 7.16 | `IsRunning_AfterSpawn_True` | Spawned container | true |
| 7.17 | `IsRunning_AfterDestroy_False` | Destroyed container | false |
| 7.18 | `SpawnContainer_MagicPaiEnvImage` | magicpai-env:latest | All tools present (claude, codex, node, dotnet) |
| 7.19 | `SpawnContainer_DockerSocket_Mounted` | MountDockerSocket=true | `docker ps` works inside |
| 7.20 | `MultipleContainers_Concurrent` | Spawn 5 simultaneously | All succeed |
| 7.21 | `Container_Filesystem_Isolation` | Write file in container A | Not visible in container B |

---

## 8. Integration Tests — Real Agent Execution

> **Prerequisite**: Claude CLI, Codex CLI installed and authenticated on machine.
> These tests verify the platform works end-to-end with real AI agents.

### 8.1 Claude Code — Real Execution

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 8.1.1 | `Claude_SimplePrompt_Succeeds` | "Create a file hello.txt with 'Hello World'" | File created, Success=true |
| 8.1.2 | `Claude_BuildCommand_ExecutesInShell` | Build command manually, run in bash | Valid stream-json output |
| 8.1.3 | `Claude_StreamJsonOutput_HasResultLine` | Run with --output-format stream-json | Last line has type=result |
| 8.1.4 | `Claude_Haiku_ModelResolution` | Model=haiku | Uses haiku-4-5-20251001 |
| 8.1.5 | `Claude_Sonnet_ModelResolution` | Model=sonnet | Uses sonnet-4-6-20250627 |
| 8.1.6 | `Claude_Opus_ModelResolution` | Model=opus | Uses opus-4-6-20250627 |
| 8.1.7 | `Claude_ParsesRealCostData` | Real execution | CostUsd > 0 |
| 8.1.8 | `Claude_ParsesRealTokenUsage` | Real execution | InputTokens > 0, OutputTokens > 0 |
| 8.1.9 | `Claude_ParsesFilesModified` | "Create 3 files" | FilesModified has entries |
| 8.1.10 | `Claude_LargePrompt_Handles` | 5000-word prompt | Completes without error |
| 8.1.11 | `Claude_MultiTurn_Completes` | Complex multi-step task | MaxTurns respected |
| 8.1.12 | `Claude_ErrorScenario_ParsedCorrectly` | Invalid task / impossible prompt | is_error or failure captured |
| 8.1.13 | `Claude_DangerouslySkipPermissions_Works` | Non-interactive mode | No permission prompts |
| 8.1.14 | `Claude_SessionId_Returned` | Real execution | Non-null session ID |
| 8.1.15 | `Claude_SpecialCharsInPrompt` | Prompt with `$`, `"`, `'`, backticks | No command injection |
| 8.1.16 | `Claude_UnicodePrompt` | Japanese/Chinese/emoji prompt | Handles correctly |
| 8.1.17 | `Claude_EmptyWorkspace` | No existing files | Works on empty dir |
| 8.1.18 | `Claude_ExistingCodebase` | Pre-populated workspace | Reads and modifies existing files |

### 8.2 Codex CLI — Real Execution

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 8.2.1 | `Codex_SimplePrompt_Succeeds` | "Create hello.py that prints hello" | File created |
| 8.2.2 | `Codex_BuildCommand_ExecutesInShell` | Build and run | Completes |
| 8.2.3 | `Codex_FullAutoApproval_NoPrompts` | --approval-mode full-auto | No interactive prompts |
| 8.2.4 | `Codex_O3Model_Works` | -m o3 | Model accepted |
| 8.2.5 | `Codex_O4MiniModel_Works` | -m o4-mini | Model accepted |
| 8.2.6 | `Codex_Gpt4oModel_Works` | -m gpt-4o | Model accepted |
| 8.2.7 | `Codex_ParseResponse_DetectsSuccess` | Clean output | Success=true |
| 8.2.8 | `Codex_ParseResponse_DetectsError` | Error in output | Success=false |
| 8.2.9 | `Codex_SpecialCharsInPrompt` | Special characters | No injection |
| 8.2.10 | `Codex_LargeCodeGeneration` | Complex code task | Completes and produces files |
| 8.2.11 | `Codex_MultiFileModification` | "Modify 3 existing files" | Changes detected |
| 8.2.12 | `Codex_ErrorRecovery` | Task with compile error, then fix | Eventually succeeds or fails gracefully |

### 8.3 Gemini CLI — Real Execution (if installed)

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 8.3.1 | `Gemini_SimplePrompt_Succeeds` | Basic task | Completes |
| 8.3.2 | `Gemini_SandboxFalse_Works` | --sandbox=false | No sandbox restrictions |
| 8.3.3 | `Gemini_25ProModel_Works` | gemini-2.5-pro | Model accepted |
| 8.3.4 | `Gemini_25FlashModel_Works` | gemini-2.5-flash | Model accepted |

### 8.4 Agent Interchangeability

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 8.4.1 | `SamePrompt_AllAgents_Produce_Valid_Output` | "Create a Python fibonacci function" | All produce valid files |
| 8.4.2 | `AgentSwitch_MidWorkflow_Works` | Triage with Claude, Execute with Codex | Pipeline completes |
| 8.4.3 | `Factory_Produces_Correct_Runner` | Each agent name | Correct runner type |

---

## 9. Integration Tests — Structured Output / Classifiers

> **CRITICAL**: JSON Schema structured output must ALWAYS work. Every classifier/triage
> call must return valid, parseable JSON matching the expected schema.

### 9.1 Triage Classifier — JSON Schema / Structured Output

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 9.1.1 | `Triage_ReturnsValidJson` | Any prompt | Output is valid JSON |
| 9.1.2 | `Triage_HasComplexityField` | Any prompt | "complexity" field exists, 1-10 integer |
| 9.1.3 | `Triage_HasCategoryField` | Any prompt | "category" is one of: code_gen, bug_fix, refactor, architecture, testing, docs |
| 9.1.4 | `Triage_HasNeedsDecomposition` | Any prompt | "needs_decomposition" is boolean |
| 9.1.5 | `Triage_HasRecommendedModel` | Any prompt | "recommended_model" is one of: haiku, sonnet, opus |
| 9.1.6 | `Triage_SimpleTask_LowComplexity` | "Add a print statement" | Complexity <= 3 |
| 9.1.7 | `Triage_MediumTask_MidComplexity` | "Add input validation to form" | Complexity 4-6 |
| 9.1.8 | `Triage_ComplexTask_HighComplexity` | "Build a full REST API with auth, DB, tests" | Complexity >= 7 |
| 9.1.9 | `Triage_CodeGenCategory` | "Write a sorting algorithm" | category=code_gen |
| 9.1.10 | `Triage_BugFixCategory` | "Fix the null reference on line 42" | category=bug_fix |
| 9.1.11 | `Triage_RefactorCategory` | "Refactor UserService into smaller methods" | category=refactor |
| 9.1.12 | `Triage_ArchitectureCategory` | "Design microservices for e-commerce" | category=architecture |
| 9.1.13 | `Triage_TestingCategory` | "Write unit tests for PaymentService" | category=testing |
| 9.1.14 | `Triage_DocsCategory` | "Write API documentation" | category=docs |
| 9.1.15 | `Triage_DecompositionTrue_ForComplex` | Complex task | needs_decomposition=true |
| 9.1.16 | `Triage_DecompositionFalse_ForSimple` | Simple task | needs_decomposition=false |
| 9.1.17 | `Triage_RecommendedModel_MatchesComplexity` | Various tasks | Low->haiku, Mid->sonnet, High->opus |
| 9.1.18 | `Triage_VaguePrompt_StillClassifies` | "fix it" | Returns valid JSON (may default) |
| 9.1.19 | `Triage_EmptyPrompt_Defaults` | "" | Falls back to defaults (5, code_gen, sonnet, false) |
| 9.1.20 | `Triage_VeryLongPrompt_StillClassifies` | 10000-char prompt | Valid JSON response |
| 9.1.21 | `Triage_NonEnglishPrompt` | Japanese prompt | Valid JSON classification |
| 9.1.22 | `Triage_MalformedResponse_Defaults` | Agent returns prose | Defaults: complexity=5, etc. |
| 9.1.23 | `Triage_PartialJson_ExtractsWhat_ItCan` | JSON missing some fields | Available fields extracted |
| 9.1.24 | `Triage_ExtraFields_Ignored` | JSON with bonus fields | Only required fields extracted |
| 9.1.25 | `Triage_100Calls_AllReturnValidJson` | 100 diverse prompts | 100% valid JSON (no parse failures) |

### 9.2 Architect Classifier — Task Decomposition JSON

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 9.2.1 | `Architect_ReturnsJsonArray` | Complex task | Valid JSON string array |
| 9.2.2 | `Architect_TasksAreStrings` | Array elements | All strings |
| 9.2.3 | `Architect_NonEmptyTasks` | Complex prompt | At least 2 tasks |
| 9.2.4 | `Architect_MaxTasksReasonable` | Very complex task | <= 10 tasks (configurable) |
| 9.2.5 | `Architect_TasksAreActionable` | Any complex task | Each task is a complete description |
| 9.2.6 | `Architect_WithGapContext` | Context provided | Tasks reference context |
| 9.2.7 | `Architect_WithoutGapContext` | No context | Still decomposes |
| 9.2.8 | `Architect_ExtractsArrayFromProse` | Array embedded in text | Array found via indexOf('[') |
| 9.2.9 | `Architect_MalformedJson_EmptyArray` | Non-JSON response | Returns empty array |
| 9.2.10 | `Architect_50Calls_AllReturnValidArrays` | 50 diverse prompts | 100% valid arrays |

### 9.3 Structured Output with output_format

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 9.3.1 | `Claude_StreamJson_EachLineIsParseable` | --output-format stream-json | Every line is valid JSON |
| 9.3.2 | `Claude_StreamJson_HasTypeField` | Stream output | Each JSON has "type" field |
| 9.3.3 | `Claude_StreamJson_ResultTypeAtEnd` | Full execution | Last relevant line is type=result |
| 9.3.4 | `StructuredOutput_TriageSchema_AlwaysValid` | Run triage 20 times | JSON matches schema 20/20 |
| 9.3.5 | `StructuredOutput_ArchitectSchema_AlwaysValid` | Run architect 10 times | JSON array 10/10 |
| 9.3.6 | `StructuredOutput_ComplexityRange_1to10` | 100 classifications | All 1 <= complexity <= 10 |
| 9.3.7 | `StructuredOutput_CategoryEnum_Valid` | 100 classifications | All in valid set |
| 9.3.8 | `StructuredOutput_BooleanFields_AreBooleans` | needs_decomposition | Always true or false, never string |
| 9.3.9 | `StructuredOutput_RepairPrompt_IsWellFormed` | Repair generation | Contains all required sections |
| 9.3.10 | `StructuredOutput_EdgeCase_PromptWithJson` | Prompt that contains JSON | Classifier not confused |
| 9.3.11 | `StructuredOutput_EdgeCase_PromptWithCode` | Prompt with code blocks | Classifier extracts correctly |

### 9.4 Structured Output Enforcement — output_format / JSON Schema

> The `output_format` parameter (e.g., `--output-format stream-json`) controls how the
> AI agent returns data. For classifiers, the prompt instructs JSON-only responses.
> These tests ensure the enforcement mechanism NEVER fails regardless of input.

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 9.4.1 | `OutputFormat_StreamJson_AlwaysSet` | ClaudeRunner.BuildCommand | `--output-format stream-json` always present |
| 9.4.2 | `OutputFormat_TriagePrompt_InstructsJsonOnly` | BuildTriagePrompt | "respond with JSON only" in prompt |
| 9.4.3 | `OutputFormat_ArchitectPrompt_InstructsJsonArray` | BuildArchitectPrompt | "Respond with a JSON array" in prompt |
| 9.4.4 | `OutputFormat_SchemaInPrompt_ComplexityField` | Triage prompt | Contains `"complexity": <1-10>` |
| 9.4.5 | `OutputFormat_SchemaInPrompt_CategoryEnum` | Triage prompt | Contains all 6 category values |
| 9.4.6 | `OutputFormat_SchemaInPrompt_BoolField` | Triage prompt | Contains `"needs_decomposition": <true/false>` |
| 9.4.7 | `OutputFormat_SchemaInPrompt_RecommendedModel` | Triage prompt | Contains haiku/sonnet/opus |
| 9.4.8 | `OutputFormat_ParseTriageResponse_ValidJson` | Real AI output | JsonDocument.Parse succeeds |
| 9.4.9 | `OutputFormat_ParseTriageResponse_TypeSafety` | complexity field | GetInt32() works, not string |
| 9.4.10 | `OutputFormat_ParseTriageResponse_BoolSafety` | needs_decomposition | GetBoolean() works, not "true" string |
| 9.4.11 | `OutputFormat_FallbackOnProse` | AI ignores JSON instruction | Defaults returned, no crash |
| 9.4.12 | `OutputFormat_FallbackOnPartialJson` | `{"complexity": 5` (truncated) | Defaults, no crash |
| 9.4.13 | `OutputFormat_FallbackOnWrappedJson` | ````json\n{...}\n`````` | Still extracted |
| 9.4.14 | `OutputFormat_MultipleTriage_NeverThrows` | 50 sequential triage calls | 0 exceptions |
| 9.4.15 | `OutputFormat_MultipleArchitect_NeverThrows` | 20 sequential architect calls | 0 exceptions |
| 9.4.16 | `OutputFormat_TriageSchema_Codex` | Codex triage (via raw prompt) | Still returns parseable result |
| 9.4.17 | `OutputFormat_TriageSchema_Gemini` | Gemini triage | Still returns parseable result |
| 9.4.18 | `OutputFormat_ArchitectSchema_Codex` | Codex architect | JSON array or graceful fallback |
| 9.4.19 | `OutputFormat_ArchitectSchema_Gemini` | Gemini architect | JSON array or graceful fallback |
| 9.4.20 | `OutputFormat_ClassifierReliability_Claude_100pct` | 100 Claude triage calls | 100% valid JSON (no parse errors) |
| 9.4.21 | `OutputFormat_ClassifierReliability_Codex_90pct` | 100 Codex triage calls | >= 90% valid JSON (fallback covers rest) |
| 9.4.22 | `OutputFormat_IsComplexApp_AlwaysClassifies` | IsComplexAppWorkflow | Returns Simple or Complex, never fails |
| 9.4.23 | `OutputFormat_IsWebsiteProject_AlwaysClassifies` | IsWebsiteProjectWorkflow | Returns Simple or Complex, never fails |
| 9.4.24 | `OutputFormat_LoopVerifier_ClassifierDecides` | LoopVerifierWorkflow triage | Correctly loops or exits |
| 9.4.25 | `OutputFormat_WebsiteAudit_PhaseChecks` | Each phase triage check | Correctly loops or advances |
| 9.4.26 | `OutputFormat_PostExecution_ReviewCheck` | Post-execution review triage | Correctly loops or advances |
| 9.4.27 | `OutputFormat_PromptEnhancer_QualityCheck` | Quality check triage | Correctly routes Simple/Complex |
| 9.4.28 | `OutputFormat_Concurrent_ClassifierCalls` | 10 parallel triage calls | All return valid JSON |

---

## 10. Integration Tests — Streaming

> **CRITICAL**: Claude Code must stream each result chunk as it arrives.
> Never buffer and wait for the full result.

### 10.1 Streaming — ExecStreamingAsync

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 10.1.1 | `Streaming_ChunksArriveIncrementally` | Long-running command | Timestamps between chunks show real-time delivery |
| 10.1.2 | `Streaming_FirstChunkBeforeCompletion` | `for i in 1..10; sleep 1; echo $i` | First chunk arrives within ~1s, not after 10s |
| 10.1.3 | `Streaming_OnOutput_CalledMultipleTimes` | Multi-chunk command | onOutput called N > 1 times |
| 10.1.4 | `Streaming_NoBuffering` | Timed output | Each chunk arrives within 500ms of generation |
| 10.1.5 | `Streaming_LargeChunks_Delivered` | Rapid output | All data delivered |
| 10.1.6 | `Streaming_UTF8_CorrectDecoding` | Unicode output | No garbled text |
| 10.1.7 | `Streaming_BinaryOutput_Handled` | Binary data | No crash (may be garbled) |
| 10.1.8 | `Streaming_EmptyChunks_Skipped` | Sparse output | No empty callbacks |

### 10.2 Streaming — Claude Agent Streaming

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 10.2.1 | `Claude_StreamJson_ChunksEmittedIncrementally` | Real Claude execution | JSON lines arrive over time |
| 10.2.2 | `Claude_Stream_AssistantMessages_Appear_InRealTime` | Long response | Text chunks arrive as generated |
| 10.2.3 | `Claude_Stream_ToolUse_Visible` | Tool use events | Visible before result |
| 10.2.4 | `Claude_Stream_NoWaitForFull` | 60-second task | Output starts within 5s |
| 10.2.5 | `Claude_Stream_EachJsonLine_IsParseable` | Full stream | Every non-empty line is valid JSON |
| 10.2.6 | `Claude_Stream_ProgressVisible_ViaSignalR` | Connected client | Receives outputChunk events in real-time |

### 10.3 Streaming — SignalR Real-Time Delivery

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 10.3.1 | `SignalR_OutputChunk_ReceivedByClient` | Connected client | outputChunk event received |
| 10.3.2 | `SignalR_Chunks_OrderPreserved` | Sequential chunks | Received in order |
| 10.3.3 | `SignalR_MultipleClients_AllReceive` | 3 clients joined | All 3 get same chunks |
| 10.3.4 | `SignalR_LateJoin_GetsBufferedOutput` | Join mid-execution | GetSessionOutput returns history |
| 10.3.5 | `SignalR_ChunksNotBatched` | Rapid output | Individual chunks, not batched |
| 10.3.6 | `SignalR_EventBridge_ForwardsAllChunks` | Agent execution | All chunks bridged |
| 10.3.7 | `SignalR_Disconnect_NoError` | Client disconnects mid-stream | Server continues without error |
| 10.3.8 | `SignalR_Reconnect_ResumesFromBuffer` | Disconnect then rejoin | Buffer provides catch-up |

### 10.4 Streaming — End-to-End

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 10.4.1 | `E2E_CreateSession_ReceiveStreamingOutput` | Create via REST, listen via SignalR | Chunks arrive in real-time |
| 10.4.2 | `E2E_NoBuffering_FirstOutputWithin10s` | Real agent task | First chunk < 10s |
| 10.4.3 | `E2E_Stream_ContinuesDuringVerification` | Verification after agent | Verification events also streamed |
| 10.4.4 | `E2E_Stream_RepairLoop_Continues` | Repair iteration | New stream starts for repair agent |

---

## 11. Integration Tests — SignalR Real-Time

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 11.1 | `Hub_Connect_Succeeds` | WebSocket connection | Connected |
| 11.2 | `Hub_CreateSession_ReturnsId` | Invoke CreateSession | Non-null session ID |
| 11.3 | `Hub_JoinSession_SubscribesEvents` | JoinSession | Receives events |
| 11.4 | `Hub_LeaveSession_StopsEvents` | LeaveSession | No more events |
| 11.5 | `Hub_StopSession_SendsStateChange` | StopSession | sessionStateChanged received |
| 11.6 | `Hub_MultipleConnections_IndependentGroups` | 2 sessions, 2 clients | Each gets only their events |
| 11.7 | `Hub_Approve_SendsConfirmation` | Approve | approvalProcessed event |
| 11.8 | `Hub_GetSessionOutput_ReturnsBuffer` | After some output | Array of text |
| 11.9 | `Hub_Disconnect_CleansUp` | Disconnect | No leaked state |
| 11.10 | `Hub_ConcurrentSessions_NoBleed` | 5 concurrent sessions | Events don't cross sessions |
| 11.11 | `Hub_VerificationUpdate_Received` | Verification completes | verificationUpdate event |
| 11.12 | `Hub_ContainerEvent_Received` | Container spawned | containerEvent event |
| 11.13 | `Hub_TaskEvent_Received` | Triage completes | taskEvent event |
| 11.14 | `Hub_CostUpdate_Received` | Agent finishes | costUpdate event |

---

## 12. Integration Tests — REST API

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 12.1 | `API_CreateSession_ValidBody_Returns200` | POST /api/sessions | 200 + session ID |
| 12.2 | `API_CreateSession_MissingBody_Returns400` | POST with no body | 400 |
| 12.3 | `API_CreateSession_EmptyPrompt_Returns400` | prompt="" | 400 or handles gracefully |
| 12.4 | `API_ListSessions_Returns200` | GET /api/sessions | 200 + array |
| 12.5 | `API_GetSession_Exists_Returns200` | GET /api/sessions/{id} | 200 + SessionInfo |
| 12.6 | `API_GetSession_NotExists_Returns404` | GET bad ID | 404 |
| 12.7 | `API_StopSession_Exists_Returns200` | DELETE /api/sessions/{id} | 200 |
| 12.8 | `API_StopSession_NotExists_Returns404` | DELETE bad ID | 404 |
| 12.9 | `API_Approve_Exists_Returns200` | POST /api/sessions/{id}/approve | 200 |
| 12.10 | `API_Approve_NotExists_Returns404` | POST bad ID | 404 |
| 12.11 | `API_GetOutput_Returns200` | GET /api/sessions/{id}/output | 200 + string[] |
| 12.12 | `API_GetOutput_NotExists_Returns404` | GET bad ID | 404 |
| 12.13 | `API_CreateSession_AllAgentTypes` | agent=claude/codex/gemini | All accepted |
| 12.14 | `API_CreateSession_AllModelTypes` | Various models | All accepted |
| 12.15 | `API_CreateSession_AllWorkflows` | full-orchestrate, simple, verify-repair | All dispatched |
| 12.16 | `API_ConcurrentRequests_ThreadSafe` | 50 concurrent POST | All succeed |
| 12.17 | `API_CORS_AllowsAnyOrigin` | Cross-origin request | Allowed |

---

## 13. Integration Tests — End-to-End Workflows

### 13.1 Full Orchestrate — Simple Path

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 13.1.1 | `E2E_SimpleTask_FullPipeline` | "Create hello.py" | Spawn->Triage(Simple)->Agent->Verify->Destroy |
| 13.1.2 | `E2E_SimpleTask_Claude` | Simple + Claude | Completes with Claude |
| 13.1.3 | `E2E_SimpleTask_Codex` | Simple + Codex | Completes with Codex |
| 13.1.4 | `E2E_SimpleTask_CompileGate_Passes` | Generates valid code | compile gate passes |
| 13.1.5 | `E2E_SimpleTask_TestGate_Passes` | With test project | test gate passes |
| 13.1.6 | `E2E_SimpleTask_AllGatesPass` | Good code | All configured gates pass |
| 13.1.7 | `E2E_SimpleTask_ContainerCleaned` | After completion | Container destroyed |
| 13.1.8 | `E2E_SimpleTask_CostTracked` | Agent runs | CostUsd > 0 |

### 13.2 Full Orchestrate — Complex Path

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 13.2.1 | `E2E_ComplexTask_TriagesAsComplex` | "Build full REST API" | Complexity >= 7 |
| 13.2.2 | `E2E_ComplexTask_ArchitectDecomposes` | Complex task | TaskCount > 1 |
| 13.2.3 | `E2E_ComplexTask_SubTasksExecuted` | Decomposed | Each sub-task runs |
| 13.2.4 | `E2E_ComplexTask_VerificationAfterEach` | Sub-tasks done | Verification runs |
| 13.2.5 | `E2E_ComplexTask_RepairOnFailure` | Gate fails | Repair loop triggered |
| 13.2.6 | `E2E_ComplexTask_ContainerCleaned` | After all | Container destroyed |

### 13.3 Simple Agent Workflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 13.3.1 | `E2E_SimpleWorkflow_Spawn_Run_Verify_Destroy` | Any prompt | Linear pipeline |
| 13.3.2 | `E2E_SimpleWorkflow_NoTriage` | Any prompt | No triage step |

### 13.4 Verify and Repair Workflow

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 13.4.1 | `E2E_VerifyRepair_PassesOnFirstTry` | Good code task | 0 repair iterations |
| 13.4.2 | `E2E_VerifyRepair_RepairsOnce` | Slightly broken | 1 repair iteration, then passes |
| 13.4.3 | `E2E_VerifyRepair_MaxAttempts_Reached` | Unfixable task | Stops after max attempts |
| 13.4.4 | `E2E_VerifyRepair_RepairPrompt_Accurate` | Gate fails | Repair prompt contains error details |

### 13.5 Cross-Cutting

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 13.5.1 | `E2E_Budget_EnforcedWhenSet` | MaxBudgetUsd=0.50 | Stops if exceeded (or at least warns) |
| 13.5.2 | `E2E_Timeout_EnforcedPerAgent` | 1-minute timeout | Agent killed on timeout |
| 13.5.3 | `E2E_Cancellation_MidExecution` | Cancel during agent run | Workflow cancelled, container cleaned |
| 13.5.4 | `E2E_HumanApproval_PausesWorkflow` | Approval gate | Workflow suspends |
| 13.5.5 | `E2E_HumanApproval_ResumesOnApprove` | Approve | Workflow continues |
| 13.5.6 | `E2E_HumanApproval_RejectsOnDeny` | Reject | Workflow takes rejection path |
| 13.5.7 | `E2E_GitWorktree_Isolation` | Parallel workers | Each gets own worktree |
| 13.5.8 | `E2E_GitWorktree_MergesBack` | Parallel done | Worktree merged to target |
| 13.5.9 | `E2E_FileClaimContention` | 2 workers same file | Only 1 claims |
| 13.5.10 | `E2E_SessionTracker_OutputBuffered` | Long execution | All output in buffer |
| 13.5.11 | `E2E_CostAccumulation_MultipleAgents` | Triage + Agent + Repair | Costs summed |

---

## 14. Edge Cases & Error Scenarios

### 14.1 Agent Execution Failures

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 14.1.1 | `Agent_NotInstalled_FailsGracefully` | codex not on PATH | Error captured, not crash |
| 14.1.2 | `Agent_AuthExpired_FailsGracefully` | Invalid API key | Error message, not hang |
| 14.1.3 | `Agent_RateLimit_Handled` | Rate limited | Error or retry |
| 14.1.4 | `Agent_NetworkDown_Handled` | No internet | Timeout and error |
| 14.1.5 | `Agent_ProducesEmptyOutput` | Agent crashes mid-run | Graceful failure |
| 14.1.6 | `Agent_ExceedsMaxTurns` | MaxTurns=1, complex task | Stops at max turns |
| 14.1.7 | `Agent_ProducesInvalidJson` | Corrupted output | ParseResponse handles |
| 14.1.8 | `Agent_OutputExceedsMemory` | Massive output | No OOM crash |
| 14.1.9 | `Agent_Timeout_KilledGracefully` | Very long task | Timeout fires, process killed |

### 14.2 Docker Failures

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 14.2.1 | `Docker_NotRunning_FailsGracefully` | Docker daemon off | Clear error message |
| 14.2.2 | `Docker_ImageNotFound_FailsGracefully` | Bad image name | Error, not hang |
| 14.2.3 | `Docker_OutOfDiskSpace` | Full disk | Error captured |
| 14.2.4 | `Docker_OutOfMemory` | Container OOM | ExitCode 137, error captured |
| 14.2.5 | `Docker_ContainerCrash_MidExec` | Container killed externally | Exec fails gracefully |
| 14.2.6 | `Docker_InvalidContainerId_ForExec` | Bad ID | Exception handled |
| 14.2.7 | `Docker_PortConflict_GuiPort` | Port already in use | Clear error |
| 14.2.8 | `Docker_WorkspacePath_NotExists` | Invalid mount path | Error captured |
| 14.2.9 | `Docker_WorkspacePath_ReadOnly` | Read-only mount | Build fails, error captured |
| 14.2.10 | `Docker_SocketPermissionDenied` | Docker socket not accessible | Error message |

### 14.3 Verification Failures

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 14.3.1 | `Gate_Timeout_DuringVerify` | Build takes 30 minutes | Gate times out |
| 14.3.2 | `Gate_ContainerDied_DuringVerify` | Container crashes | Gate fails gracefully |
| 14.3.3 | `Gate_NoProjectFiles_Skip` | Empty workspace | CanVerify=false, skipped |
| 14.3.4 | `Pipeline_AllGatesSkipped` | Nothing to verify | Inconclusive or pass |
| 14.3.5 | `Gate_OutputTooLarge` | 1GB build output | Handled without OOM |

### 14.4 Workflow Failures

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 14.4.1 | `Workflow_ContainerSpawnFails_WorkflowFails` | Docker down | Workflow terminates |
| 14.4.2 | `Workflow_AgentFails_RepairPath` | Agent error | Triggers repair or fails |
| 14.4.3 | `Workflow_AllRepairAttemptsFail` | Unfixable code | Terminates after max attempts |
| 14.4.4 | `Workflow_CancelledMidAgent` | External cancel | Clean shutdown |
| 14.4.5 | `Workflow_Orphaned_Container_Cleanup` | Workflow crashes | Container eventually cleaned |
| 14.4.6 | `Workflow_TriageFails_DefaultsToSimple` | Triage exception | Falls back to simple path |

### 14.5 Input Validation

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 14.5.1 | `NullPrompt_Handled` | Prompt=null | Error or default |
| 14.5.2 | `EmptyPrompt_Handled` | Prompt="" | Error or default |
| 14.5.3 | `VeryLongPrompt_Handled` | 1MB prompt | Truncated or error |
| 14.5.4 | `InvalidAgentName_Handled` | Agent="xyzzy" | ArgumentException |
| 14.5.5 | `InvalidModelName_Handled` | Model="nonexistent" | Passed as literal or error |
| 14.5.6 | `NegativeMaxTurns_Handled` | MaxTurns=-1 | Default or error |
| 14.5.7 | `ZeroTimeout_Handled` | Timeout=0 | Immediate timeout or error |
| 14.5.8 | `NegativeMemoryLimit_Handled` | MemoryLimitMb=-1 | Error |
| 14.5.9 | `PathTraversal_InWorkspace` | WorkDir="../../etc" | Blocked or sandboxed |
| 14.5.10 | `CommandInjection_InPrompt` | Prompt with shell escape chars | No injection |
| 14.5.11 | `PromptWithJsonPayload` | Prompt that looks like JSON | Not confused |
| 14.5.12 | `PromptWithSqlPayload` | SQL injection attempt | Blocked by security gate |

---

## 15. Performance & Stress Tests

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 15.1 | `ConcurrentSessions_10` | 10 simultaneous sessions | All complete |
| 15.2 | `ConcurrentSessions_50` | 50 simultaneous sessions | Server responsive |
| 15.3 | `SignalR_100Clients_SingleSession` | 100 clients on 1 session | All receive events |
| 15.4 | `OutputBuffer_1000Entries` | Max buffer size | Circular buffer works |
| 15.5 | `OutputBuffer_Overflow` | 2000 entries into 1000 buffer | Oldest dropped |
| 15.6 | `SharedBlackboard_1000ConcurrentClaims` | 1000 threads | Thread-safe |
| 15.7 | `Docker_5ConcurrentContainers` | Spawn 5 | All start |
| 15.8 | `Docker_RapidSpawnDestroy` | Spawn+Destroy 50 times | No leaks |
| 15.9 | `LargeWorkspace_1GB` | Mount 1GB directory | Container handles |
| 15.10 | `API_RapidRequests_100` | 100 POST/s | No 500 errors |
| 15.11 | `VerificationPipeline_7Gates` | All 7 gates enabled | Completes in reasonable time |
| 15.12 | `StreamingOutput_HighVolume` | 1000 chunks/second | No dropped chunks |
| 15.13 | `SessionTracker_1000Sessions` | Register 1000 | All trackable |
| 15.14 | `Memory_NoLeaks_AfterWorkflows` | Run 100 workflows | Memory stable |

---

## 16. Security Tests

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 16.1 | `CommandInjection_ViaPrompt` | Prompt: `'; rm -rf / #` | No execution of injected command |
| 16.2 | `CommandInjection_ViaWorkDir` | WorkDir: `/workspace; rm -rf /` | Blocked |
| 16.3 | `CommandInjection_ViaContainerId` | ID with shell chars | Blocked |
| 16.4 | `PathTraversal_ViaWorkspacePath` | `../../etc/passwd` | Sandboxed |
| 16.5 | `DockerSocket_Abuse` | Container tries host breakout | Contained (no socket by default) |
| 16.6 | `SecurityGate_CatchesAllPatterns` | Code with all OWASP issues | All flagged |
| 16.7 | `API_NoAuth_OnEndpoints` | Unauthenticated requests | Server handles (CORS open by design) |
| 16.8 | `SignalR_SessionIsolation` | Client guesses session ID | Cannot join without invite |
| 16.9 | `ContainerEscape_Blocked` | Privileged operations | Container config blocks |
| 16.10 | `SecretInWorkspace_Flagged` | .env with API key | SecurityGate catches |
| 16.11 | `DangerouslySkipPermissions_OnlyInContainer` | Flag set | Only applies inside container |

---

## 17. Configuration Tests

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 17.1 | `Config_DefaultValues_AllSet` | New MagicPaiConfig | All have defaults |
| 17.2 | `Config_UseDocker_False_UsesLocal` | UseDocker=false | LocalExecutionEnvironment |
| 17.3 | `Config_DefaultAgent_Overridden` | DefaultAgent="codex" | Sessions default to codex |
| 17.4 | `Config_MaxRepairAttempts_Respected` | MaxRepairAttempts=2 | Only 2 repair loops |
| 17.5 | `Config_DefaultGates_Applied` | DefaultGates=["compile"] | Only compile gate runs |
| 17.6 | `Config_CoverageThreshold_Applied` | CoverageThreshold=90 | 80% fails |
| 17.7 | `Config_MaxBudget_Enforced` | MaxBudgetUsd=1.00 | Stops at $1 |
| 17.8 | `Config_MaxBudget_Zero_Unlimited` | MaxBudgetUsd=0 | No limit |
| 17.9 | `Config_ComplexityThreshold_Changed` | ComplexityThreshold=5 | 5 is complex |
| 17.10 | `Config_MaxSubTasks_Limit` | MaxSubTasks=3 | Architect produces max 3 |
| 17.11 | `Config_MaxParallelWorkers` | MaxParallelWorkers=1 | Serial execution |
| 17.12 | `Config_SignalRBufferSize_Applied` | SignalRBufferSize=100 | Buffer caps at 100 |
| 17.13 | `Config_WorkerImage_Custom` | WorkerImage="custom:v1" | Custom image used |
| 17.14 | `Config_ContainerTimeout_Applied` | ContainerTimeoutMinutes=5 | Container killed after 5m |
| 17.15 | `Config_ModelOverrides_Applied` | ModelOverrides {"haiku":"custom"} | Override used |
| 17.16 | `Config_EnableAdaptiveRouting` | EnableAdaptiveRouting=true | ModelRouter active |
| 17.17 | `Config_EnableSecurityScan_False` | EnableSecurityScan=false | SecurityGate skipped |
| 17.18 | `Config_EnableWorktreeIsolation_False` | EnableWorktreeIsolation=false | No worktrees |
| 17.19 | `Config_AppsettingsJson_LoadsCorrectly` | Real appsettings.json | All values bound |

---

## 18. Docker Infrastructure Tests

> **Prerequisite**: Docker Compose and Docker Desktop available.

### 18.1 Worker Image (magicpai-env)

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 18.1.1 | `WorkerImage_Builds` | docker build worker-env | Image builds successfully |
| 18.1.2 | `WorkerImage_HasNode24` | node --version | v24.x |
| 18.1.3 | `WorkerImage_HasDotnet10` | dotnet --version | 10.x |
| 18.1.4 | `WorkerImage_HasDotnet9` | dotnet --list-sdks | 9.x present |
| 18.1.5 | `WorkerImage_HasDotnet8` | dotnet --list-sdks | 8.x present |
| 18.1.6 | `WorkerImage_HasGo` | go version | 1.24.x |
| 18.1.7 | `WorkerImage_HasRust` | rustc --version | Stable |
| 18.1.8 | `WorkerImage_HasPython3` | python3 --version | 3.x |
| 18.1.9 | `WorkerImage_HasGit` | git --version | Present |
| 18.1.10 | `WorkerImage_HasClaudeCli` | claude --version | Installed |
| 18.1.11 | `WorkerImage_HasCodexCli` | codex --version | Installed (or graceful skip) |
| 18.1.12 | `WorkerImage_HasPlaywright` | npx playwright --version | Present with Chromium |
| 18.1.13 | `WorkerImage_HasDockerCli` | docker --version | CLI present |
| 18.1.14 | `WorkerImage_NonRootUser` | whoami | "worker" |
| 18.1.15 | `WorkerImage_WorkspaceDir` | pwd | /workspace |
| 18.1.16 | `WorkerImage_HasJq` | jq --version | Present |
| 18.1.17 | `WorkerImage_HasNoVNC` | ls /usr/share/novnc | Present |
| 18.1.18 | `WorkerImage_EntrypointWorks` | Container starts | No crash |

### 18.2 Docker Compose Stack

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 18.2.1 | `Compose_AllServicesStart` | docker-compose up | server + db both healthy |
| 18.2.2 | `Compose_ServerPort5000` | curl localhost:5000 | Responds |
| 18.2.3 | `Compose_PostgresPort5432` | pg_isready -h localhost | Ready |
| 18.2.4 | `Compose_DockerSocketMounted` | Server can manage containers | docker.sock works |
| 18.2.5 | `Compose_WorkspacesVolume` | Volume exists | Persistent storage |
| 18.2.6 | `Compose_EnvVarsPassedToServer` | MagicPAI config | Config values match compose env |
| 18.2.7 | `Compose_DbConnectionString` | EF Core migration | DB accessible |
| 18.2.8 | `Compose_WorkerEnvBuilder` | --profile build | magicpai-env:latest image created |
| 18.2.9 | `Compose_Restart_Recovers` | Kill server, wait | Auto-restarts |
| 18.2.10 | `Compose_DataPersistsRestart` | Create session, restart, check | Session data preserved |
| 18.2.11 | `ComposeDev_UseSQLite` | dev override | Data Source=magicpai.db |
| 18.2.12 | `ComposeDev_UseDockerFalse` | dev override | MagicPAI__UseDocker=false |
| 18.2.13 | `ComposeDev_NoPostgres` | dev profile | No db service |
| 18.2.14 | `ComposeDev_SourceMounted` | dev override | /src:ro mount |

### 18.2b Server Dockerfile

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 18.2b.1 | `ServerImage_Builds` | docker build | Multi-stage builds |
| 18.2b.2 | `ServerImage_DotnetPublish` | Runtime image | MagicPAI.Server.dll exists |
| 18.2b.3 | `ServerImage_HasDockerCli` | docker --version | CLI installed |
| 18.2b.4 | `ServerImage_ExposesPort8080` | Port config | 8080 exposed |
| 18.2b.5 | `ServerImage_ExposesPort8081` | Port config | 8081 exposed |
| 18.2b.6 | `ServerImage_Entrypoint` | Container starts | Server starts |

### 18.2c Worker Entrypoint

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 18.2c.1 | `Entrypoint_StartsXvfb` | Container starts | Display :99 available |
| 18.2c.2 | `Entrypoint_StartsFluxbox` | Container starts | Window manager running |
| 18.2c.3 | `Entrypoint_StartsVNC` | Container starts | Port 5900 listening |
| 18.2c.4 | `Entrypoint_StartsNoVNC` | Container starts | Port 7900 listening |
| 18.2c.5 | `Entrypoint_DockerSocketPermissions` | Docker socket mounted | worker user can run docker |
| 18.2c.6 | `Entrypoint_ExecutesCommand` | Pass command as arg | Command executed |
| 18.2c.7 | `Entrypoint_WaitsWithoutCommand` | No args | Stays running |

### 18.3 Event Models

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 18.3.1 | `OutputChunkEvent_RecordEquality` | Two identical | Equal |
| 18.3.2 | `OutputChunkEvent_OptionalActivityName` | null ActivityName | No crash |
| 18.3.3 | `WorkflowProgressEvent_AllFields` | Full construction | All set |
| 18.3.4 | `VerificationUpdateEvent_WithIssues` | issues array | Preserved |
| 18.3.5 | `CostUpdateEvent_DecimalPrecision` | 0.001234 | Preserved |
| 18.3.6 | `SessionStateEvent_RecordEquality` | Two identical | Equal |
| 18.3.7 | `ContainerEvent_WithGuiUrl` | GuiUrl set | Preserved |
| 18.3.8 | `ContainerEvent_NullGuiUrl` | No GUI | null |
| 18.3.9 | `ErrorEvent_OptionalActivityName` | null | No crash |
| 18.3.10 | `SessionInfo_AllPropertiesSettable` | Set all | All retrievable |

---

## 19. Blazor Studio Frontend Tests

> **Prerequisite**: MagicPAI.Studio Blazor WASM project builds. Some tests require bUnit or Playwright.

### 19.1 SessionHubClient

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.1.1 | `ConnectAsync_EstablishesConnection` | Call ConnectAsync | State=Connected |
| 19.1.2 | `ConnectAsync_Idempotent` | Call twice | No error, only connects once |
| 19.1.3 | `CreateSessionAsync_ReturnsId` | Valid params | Non-null session ID string |
| 19.1.4 | `StopSessionAsync_InvokesHub` | Valid session | Hub StopSession invoked |
| 19.1.5 | `ApproveAsync_InvokesHub` | Session + decision | Hub Approve invoked |
| 19.1.6 | `OnOutputChunk_EventFires` | Server sends outputChunk | Event handler called |
| 19.1.7 | `OnWorkflowProgress_EventFires` | Server sends workflowProgress | Event handler called |
| 19.1.8 | `OnVerificationUpdate_EventFires` | Server sends verificationUpdate | Event handler called |
| 19.1.9 | `OnCostUpdate_EventFires` | Server sends costUpdate | Event handler called |
| 19.1.10 | `OnSessionStateChanged_EventFires` | Server sends sessionStateChanged | Event handler called |
| 19.1.11 | `OnContainerSpawned_EventFires` | Server sends containerSpawned | Event handler called |
| 19.1.12 | `OnError_EventFires` | Server sends error | Event handler called |
| 19.1.13 | `AutomaticReconnect_Configured` | Connection config | Reconnect intervals: 0, 1, 3, 5s |
| 19.1.14 | `HubUrl_FromConfig` | Config["MagicPAI:HubUrl"] | Uses configured URL |
| 19.1.15 | `HubUrl_DefaultsToLocalhost` | No config | "http://localhost:5000/hub" |
| 19.1.16 | `DisposeAsync_CleansUp` | Dispose | No leaked connections |
| 19.1.17 | `EventTypes_StronglyTyped` | All events | Deserialized to correct record types |

### 19.2 SessionApiClient

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.2.1 | `ListSessionsAsync_Returnslist` | API responds | List<SessionInfo> |
| 19.2.2 | `ListSessionsAsync_HttpError_ReturnsEmpty` | Server down | Empty list, no crash |
| 19.2.3 | `GetSessionAsync_Found` | Valid ID | SessionInfo object |
| 19.2.4 | `GetSessionAsync_NotFound` | Bad ID | null |
| 19.2.5 | `GetSessionAsync_HttpError_ReturnsNull` | Server down | null, no crash |
| 19.2.6 | `DeleteSessionAsync_Success` | Valid ID | true |
| 19.2.7 | `DeleteSessionAsync_NotFound` | Bad ID | false |
| 19.2.8 | `DeleteSessionAsync_HttpError` | Server down | false, no crash |

### 19.3 Dashboard Page

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.3.1 | `Renders_QuickStartSection` | Page load | Textarea + button visible |
| 19.3.2 | `Renders_EmptySessionList` | No sessions | "No sessions yet" message |
| 19.3.3 | `Renders_SessionCards` | Sessions exist | Cards with ID, agent, cost, time |
| 19.3.4 | `StartSession_EmptyPrompt_NoAction` | Empty textarea, click Start | Nothing happens |
| 19.3.5 | `StartSession_ValidPrompt_CreatesSession` | Enter prompt, click Start | Hub.CreateSessionAsync called |
| 19.3.6 | `StartSession_DisablesButton` | Click Start | Button shows "Starting..." |
| 19.3.7 | `StartSession_NavigatesToSession` | Success | Navigates to /magic/sessions/{id} |
| 19.3.8 | `StartSession_HubError_Recovers` | Hub throws | Error logged, no crash |
| 19.3.9 | `SessionStateChanged_UpdatesList` | State event | Card state badge updates |
| 19.3.10 | `AgentSelector_DefaultsClaude` | Initial render | "claude" selected |
| 19.3.11 | `AgentSelector_CanSwitchToCodex` | Select codex | selectedAgent = "codex" |
| 19.3.12 | `HubUnavailable_PageStillRenders` | Hub not running | No crash, sessions from API |

### 19.4 SessionView Page

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.4.1 | `Renders_OutputPanel` | Page load | OutputPanel component visible |
| 19.4.2 | `Renders_Sidebar` | Page load | Status, cost, container, DAG visible |
| 19.4.3 | `OutputChunk_AppendsToPanel` | Receive chunk | Text appended to output |
| 19.4.4 | `OutputChunk_FiltersToSessionId` | Chunk for different session | Ignored |
| 19.4.5 | `StateChanged_UpdatesBadge` | State event | Status badge updates |
| 19.4.6 | `VerificationUpdate_ShowsBadge` | Verification event | VerificationBadge appears |
| 19.4.7 | `WorkflowProgress_UpdatesDAG` | Progress event | DagView updated |
| 19.4.8 | `ContainerEvent_ShowsContainerId` | Container event | ContainerStatus shows ID |
| 19.4.9 | `Error_AppendsToOutput` | Error event | "[ERROR]" text appended |
| 19.4.10 | `Stop_CallsHub` | Click Stop | Hub.StopSessionAsync called |
| 19.4.11 | `Stop_OnlyVisibleWhenRunning` | State != running | Button hidden |
| 19.4.12 | `StreamingOutput_RendersInRealTime` | Multiple chunks over time | Each chunk rendered immediately |
| 19.4.13 | `LargeOutput_NoPerformanceIssue` | 10000 chunks | Page still responsive |

### 19.5 OutputPanel Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.5.1 | `AppendText_AddsContent` | Call AppendText("hi") | "hi" visible |
| 19.5.2 | `AppendText_AccumulatesContent` | 3 calls | All text concatenated |
| 19.5.3 | `Clear_ResetsContent` | Clear after appending | Empty |
| 19.5.4 | `AppendText_TriggersStateHasChanged` | Call AppendText | UI re-renders |

### 19.6 AgentSelector Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.6.1 | `Renders_ThreeOptions` | Initial | claude, codex, gemini |
| 19.6.2 | `DefaultValue_Claude` | No override | "claude" selected |
| 19.6.3 | `Selection_TriggersValueChanged` | Select "codex" | EventCallback fired |
| 19.6.4 | `TwoWayBinding_Works` | Parent changes Value | Select updates |

### 19.7 CostTracker Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.7.1 | `Renders_CostValue` | Initial | $0.0000 |
| 19.7.2 | `CostUpdate_UpdatesDisplay` | CostUpdateEvent | New cost displayed |
| 19.7.3 | `CostUpdate_FiltersSessionId` | Event for other session | Unchanged |
| 19.7.4 | `TokenCounts_Updated` | Event with tokens | In/Out counts shown |

### 19.8 VerificationBadge Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.8.1 | `Passed_ShowsPassBadge` | Passed=true | "PASS" and green class |
| 19.8.2 | `Failed_ShowsFailBadge` | Passed=false | "FAIL" and red class |
| 19.8.3 | `ShowsGateName` | GateName="compile" | "compile" visible |
| 19.8.4 | `ShowsOutput_WhenPresent` | Output="OK" | Output shown |
| 19.8.5 | `HidesOutput_WhenEmpty` | Output="" | No output div |
| 19.8.6 | `ShowsIssues_WhenPresent` | 3 issues | 3 list items |
| 19.8.7 | `HidesIssues_WhenEmpty` | Issues=[] | No issues list |

### 19.9 ContainerStatus Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.9.1 | `NoContainer_ShowsIdle` | ContainerId="" | "No Container" |
| 19.9.2 | `WithContainer_ShowsTruncatedId` | 64-char ID | First 12 chars shown |
| 19.9.3 | `ShortId_ShownFull` | 8-char ID | Full ID shown |

### 19.10 DagView Component

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.10.1 | `EmptyActivities_ShowsMessage` | Activities=[] | "No activity data yet" |
| 19.10.2 | `Running_ShowsAsterisk` | Status="running" | "[*]" icon |
| 19.10.3 | `Completed_ShowsPlus` | Status="completed" | "[+]" icon |
| 19.10.4 | `Done_ShowsPlus` | Status="done" | "[+]" icon |
| 19.10.5 | `Failed_ShowsX` | Status="failed" | "[x]" icon |
| 19.10.6 | `Pending_ShowsEmpty` | Status="pending" | "[ ]" icon |
| 19.10.7 | `Unknown_ShowsQuestion` | Status="foobar" | "[?]" icon |
| 19.10.8 | `MultipleActivities_AllRendered` | 5 activities | 5 nodes rendered |

### 19.11 CostDashboard Page

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.11.1 | `Renders_SummaryCards` | Page load | 4 summary cards |
| 19.11.2 | `Renders_SessionTable` | Sessions exist | Table rows |
| 19.11.3 | `TotalCost_Summed` | 3 sessions | Sum of all costs |
| 19.11.4 | `CostUpdate_RefreshesTotal` | Live event | Total recalculated |
| 19.11.5 | `TokenCounts_Accumulated` | Multiple events | Running totals |
| 19.11.6 | `Sessions_OrderedByDate` | Various dates | Newest first |

### 19.12 Settings Page

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.12.1 | `Renders_AllSections` | Page load | Agent, verification, docker sections |
| 19.12.2 | `DefaultAgent_Claude` | Initial | "claude" selected |
| 19.12.3 | `ModelDropdown_UpdatesWithAgent` | Switch to codex | Shows o3, o4-mini, gpt-4o |
| 19.12.4 | `GateCheckboxes_AllPresent` | Initial | 7 gate checkboxes |
| 19.12.5 | `GateCheckboxes_DefaultsCorrect` | Initial | compile, test, hallucination checked |
| 19.12.6 | `ToggleGate_AddsToSet` | Check coverage | enabledGates contains "coverage" |
| 19.12.7 | `ToggleGate_RemovesFromSet` | Uncheck compile | enabledGates lacks "compile" |
| 19.12.8 | `SaveSettings_ShowsNotice` | Click Save | "Settings saved" message |
| 19.12.9 | `MaxTurns_NumberInput` | Set to 50 | maxTurns=50 |
| 19.12.10 | `WorkerImage_TextInput` | Set custom | workerImage updated |

### 19.13 MagicPaiMenuProvider

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.13.1 | `GetMenuItems_Returns4Items` | Call | 4 menu items |
| 19.13.2 | `MenuItem_Dashboard` | First item | Href="magic/dashboard", GroupName="MagicPAI" |
| 19.13.3 | `MenuItem_Sessions` | Second item | Href="magic/sessions" |
| 19.13.4 | `MenuItem_CostAnalytics` | Third item | Href="magic/costs" |
| 19.13.5 | `MenuItem_Settings` | Fourth item | Href="magic/settings" |
| 19.13.6 | `MenuItems_OrderDescending` | All items | Order: -10, -9, -8, -7 |
| 19.13.7 | `AllItems_HaveIcons` | All items | Icon is non-empty |

### 19.14 Studio DI & Startup

| # | Test Name | Description | Expected |
|---|-----------|-------------|----------|
| 19.14.1 | `SessionHubClient_RegisteredScoped` | DI | Available |
| 19.14.2 | `SessionApiClient_RegisteredScoped` | DI | Available |
| 19.14.3 | `MenuProvider_RegisteredScoped` | DI | MagicPaiMenuProvider |
| 19.14.4 | `ElsaStudio_CoreRegistered` | Services | Elsa Studio modules loaded |
| 19.14.5 | `BackendUrl_Configurable` | Config | Reads from Elsa:Server:BaseUrl |
| 19.14.6 | `DummyAuthHandler_PassesThrough` | Send request | Request forwarded unchanged |

---

## Test Execution Matrix

| Test Category | Count | Type | Docker Required | Agent Required |
|---|---|---|---|---|
| 1. Core Services | ~100 | Unit | No | No |
| 2. Verification Gates | ~50 | Unit | No | No |
| 3. Activities | ~65 | Unit | No | No |
| 4. Models & DTOs | ~14 | Unit | No | No |
| 5. Server Components | ~80 | Unit | No | No |
| 6. Workflows | ~85 | Unit | No | No |
| 7. Docker Integration | ~21 | Integration | Yes | No |
| 8. Real Agent Execution | ~35 | Integration | Yes | Yes (Claude + Codex) |
| 9. Structured Output | ~36 | Integration | Yes | Yes |
| 10. Streaming | ~22 | Integration | Yes | Yes |
| 11. SignalR Real-Time | ~14 | Integration | No | No |
| 12. REST API | ~17 | Integration | No | No |
| 13. E2E Workflows | ~25 | E2E | Yes | Yes |
| 14. Edge Cases | ~35 | Mixed | Mixed | Mixed |
| 15. Performance | ~14 | Stress | Yes | Yes |
| 16. Security | ~11 | Security | Yes | No |
| 17. Configuration | ~19 | Unit/Integration | No | No |
| 18. Docker Infrastructure | ~38 | Integration | Yes | No |
| 19. Blazor Studio Frontend | ~110 | Unit/bUnit | No | No |
| **TOTAL** | **~791** | | | |

---

## Test Priority Levels

### P0 — Must Pass Before Any Release
- All unit tests (sections 1-6)
- Streaming tests (section 10) — Claude Code must stream, never buffer
- Structured output classifier tests (section 9) — JSON must always be valid
- Real Claude agent execution (8.1)
- Real Codex agent execution (8.2)
- Core API tests (section 12)

### P1 — Must Pass Before Production
- Docker integration (section 7)
- SignalR real-time (section 11)
- E2E workflows (section 13)
- Edge cases (section 14)
- Security tests (section 16)
- Docker infrastructure / worker image (section 18)
- DI registration (section 5.6)
- WorkflowProgressTracker (section 5.5)

### P2 — Should Pass
- Performance tests (section 15)
- Configuration tests (section 17)
- Gemini tests (8.3)
- Event model tests (section 18.3)
- Blazor Studio frontend tests (section 19)

---

## Running the Tests

```bash
# Unit tests only (fast, no Docker/agents needed)
dotnet test MagicPAI.Tests --filter "Category=Unit"

# Integration tests (Docker required)
dotnet test MagicPAI.Tests --filter "Category=Integration"

# Real agent tests (Claude + Codex must be installed and authenticated)
dotnet test MagicPAI.Tests --filter "Category=RealAgent"

# Streaming-specific tests
dotnet test MagicPAI.Tests --filter "Category=Streaming"

# Structured output / classifier tests
dotnet test MagicPAI.Tests --filter "Category=StructuredOutput"

# All tests
dotnet test MagicPAI.Tests

# Specific test class
dotnet test MagicPAI.Tests --filter "FullyQualifiedName~ClaudeRunnerTests"
```

---

## Key Validation Criteria

### Streaming Requirement
Every test that invokes Claude Code MUST verify:
1. `ExecStreamingAsync` is used (NOT `ExecAsync`) for agent execution
2. `onOutput` callback fires before the command completes
3. SignalR `outputChunk` events are emitted per-chunk, not batched
4. First chunk arrives within seconds, not after full completion

### Structured Output Requirement
Every classifier/triage test MUST verify:
1. Response is valid JSON (parseable by `JsonDocument.Parse`)
2. All required fields present with correct types
3. Enum values within expected set
4. Numeric ranges respected (complexity 1-10)
5. Fallback to defaults on malformed response (NEVER crash)
6. 100% success rate over N repeated calls

### Real Agent Verification
Both Claude and Codex MUST be tested on the actual machine:
1. CLI tool is on PATH and runnable
2. Authentication is valid (API key / session)
3. Models resolve correctly
4. Output format is parseable
5. Cost and token data extracted (Claude)
6. File modifications tracked
7. Special characters in prompts don't break execution
