# Research Step Verification — FullOrchestrateWorkflow

- Date: 2026-04-20
- Branch: `temporal`
- Session ID: `mpai-322d84e0da004718ab3f79dd1026b56f`
- Final status: **Failed** (child `SimpleAgentWorkflow` container spawn failed on port collision — unrelated to research)

## Code audit — `AiActivities.ResearchPromptAsync`

File: `MagicPAI.Activities/AI/AiActivities.cs`, lines 629-779.

- Takes `ContainerId` as required input — throws `ApplicationFailureException(type="ConfigError", nonRetryable=true)` if empty (line 631-634). CONFIRMED.
- Uses streaming via `_docker.ExecStreamingAsync(input.ContainerId, plan.MainRequest, OnOutput, timeout, ct)` (line 724) with heartbeat every 20 lines (`if (lineCount % 20 == 0) ctx.Heartbeat(lineCount)`, line 716-717). CONFIRMED.
- ModelPower defaults to **2** (`input.ModelPower > 0 ? input.ModelPower : 2`, line 674). `FullOrchestrateWorkflow` passes `ModelPower: 2` (line 144). **NOT 1** (task spec implied strongest=1). This is a discrepancy with the task description — sonnet-class balanced model is used, not opus-class.
- Prompt asks for four markdown H2 sections: `## Rewritten Task`, `## Codebase Analysis`, `## Research Context`, `## Rationale` (line 663-671). CONFIRMED (spec mentioned 3, implementation uses 4).
- `SplitResearchOutput` (line 1025-1066) splits on `"\n## "` and maps to the `ResearchPromptOutput` record. CONFIRMED.
- `AgentRequest.SessionId = null` on the research request (line 681). **Fix for the `--resume` bug IS in place.** CONFIRMED. The same nullification is applied to `TriageAsync` (line 266), `ClassifyAsync` (line 363), `EnhancePromptAsync` (line 481), `ArchitectAsync` (line 572), `GradeCoverageAsync` (line 848). `RunCliAgentAsync` preserves `input.AssistantSessionId` (a separate CLI-provider UUID, not the MagicPAI workflow id) — correct.

## Code audit — `FullOrchestrateWorkflow`

File: `MagicPAI.Server/Workflows/FullOrchestrateWorkflow.cs`.

- Research is invoked at line 147 after website classification and before triage.
- `research.EnhancedPrompt` is threaded into Triage (line 155).
- `finalPrompt = _injectedPrompt ?? research.EnhancedPrompt` (line 167) is passed to both child paths:
  - Complex path: `OrchestrateComplexInput.Prompt = finalPrompt` (line 176).
  - Simple path: `SimpleAgentInput.Prompt = finalPrompt` (line 199).
- Wiring is correct — research output IS consumed downstream.

## E2E test result

Dispatched `POST /api/sessions` with `WorkflowType=FullOrchestrate`, workspace `/workspace`, prompt: *"Summarize the current architecture of MagicPAI in 5 bullet points based on actual source files in the repo."*

Temporal history (`docker exec mpai-temporal temporal ... workflow show ... --output json`):

| Event | Activity | Status | Result size |
|------|----------|--------|------------|
| 7 | `Spawn` | Completed | ContainerId returned |
| 13 | `ClassifyWebsiteTask` | Completed | `IsWebsiteTask: false` (0.95 confidence) |
| 19 | `ResearchPrompt` | Completed | 282,757 bytes |
| 25 | `Triage` | Completed | `Complexity: 5, Category: code_gen, IsComplex: false` (likely heuristic fallback) |
| 31 | `SimpleAgentWorkflow` (child) | **Failed** | `Bind for 127.0.0.1:6080 failed: port is already allocated` |
| 40 | `Destroy` | Completed | parent cleanup |

Workflow completion time: ~83 s.

## Research activity output (`ResearchPromptOutput` record)

| Field | Length | Content |
|------|--------|---------|
| `EnhancedPrompt` | 196,481 chars | **Raw Claude stream-json protocol**, starts with `{"type":"system","subtype":"init",...}`, contains all 945 stream events concatenated |
| `CodebaseAnalysis` | **0 chars** | Empty |
| `ResearchContext` | **0 chars** | Empty |
| `Rationale` | **0 chars** | Empty |

## Root cause — stream-json parse pipeline is broken on Windows

Claude CLI emits a final event of the form:
```
{"type":"result","subtype":"success","is_error":false,...,"result":"## Rewritten Task\n\n**Research the actual source files present in `/workspace`..."}
```

The payload **does contain accurate, codebase-grounded research**. Claude correctly identified that `/workspace` inside the container held only `fib.py` (7-line fibonacci) and `.mcp.json` (Playwright MCP config), and honestly refused to fabricate a MagicPAI architecture. The four sections (Rewritten Task, Codebase Analysis, Research Context, Rationale) are all present and well-structured inside the `result` string.

But the Windows docker exec pipeline wraps long output lines at ~256 chars, injecting `\r\n` mid-string. Evidence: the captured stream contains `arch\r\nitecture`, `fib.\r\npy`, `maxOutputTokens"\r\n:32000` — newlines injected inside JSON string values and between JSON keys. This breaks the final `{"type":"result",...}` line into multiple pieces.

Consequences (fail chain):

1. `DockerContainerManager.ExecStreamingAsync` stdout reader delivers the wrapped bytes. Chunks contain `\r\n` mid-JSON.
2. `AiActivities.ResearchPromptAsync.OnOutput` does `chunk.Split('\n')` and `captured.AppendLine(line)` — re-assembles with `\r\n` separators, preserving the corruption.
3. `ClaudeRunner.ParseResponse` (`MagicPAI.Core/Services/ClaudeRunner.cs:120`) does `rawOutput.Split('\n', RemoveEmptyEntries).Select(TryParseJson)` → zero lines parse as valid JSON with `type="result"` → `ParseResponse` returns `(Success:false, Output:rawOutput, ...)`.
4. In the activity, `parsed.Output` = the 196KB raw blob → `responseText = parsed.Output`.
5. `SplitResearchOutput` splits on `"\n## "`. The `##` markers are embedded inside `"text":"...\n## Rewritten Task..."` JSON values, not on clean line starts → no sections match → fallback path assigns `rewritten = output.Trim()`, other three fields stay empty.
6. `EnhancedPrompt` is the 196KB stream-json blob; `CodebaseAnalysis`, `ResearchContext`, `Rationale` are empty.

## Quality rating: **1 / 5**

- 5 = substantial codebase-grounded output delivered to the workflow.
- 1 = research output is essentially the original prompt or empty noise.

The LLM's actual research was a 4/5 (it honestly reported workspace was empty of MagicPAI source and refused to hallucinate). But what the **activity returned to the workflow** was unusable — a raw stream-json blob masquerading as the enhanced prompt. Downstream consumers received garbage.

## Wiring assessment — research output IS consumed downstream, amplifying the bug

The `FullOrchestrateWorkflow` wiring is correct. This made things worse, not better:

- **Triage** received the 196KB stream-json blob as its `Prompt` (verified in Temporal input payload: 196,481 chars starting with `{"type":"system","subtype":"init"...`). It still returned `Complexity: 5, IsComplex: false` — almost certainly from the heuristic fallback in `FallbackTriageResult` because Triage's own Claude call either failed or produced unparsable output on that input.
- **SimpleAgent child workflow** received the same 196KB blob as its `Prompt` (verified in `START_CHILD_WORKFLOW_EXECUTION_INITIATED` payload). Had the container spawn not failed on the port collision, the downstream agent would have been asked to "do coding work" with Claude's own stream-json init event as its instructions.

## Secondary bug — child workflow spawns duplicate container

`SimpleAgentWorkflow` tries to spawn its own container even though `FullOrchestrateWorkflow` already owns one. The collision surfaced on VNC port 6080 because both containers request the same fixed port binding. This is an orthogonal issue but currently prevents the E2E chain from completing even when research works.

## Recommended fixes

1. **Stop splitting the stream by `\n` in `ResearchPromptAsync.OnOutput`.** Append the chunk verbatim to `captured` (preserve exact bytes), and only split for heartbeat accounting. Use `result.Output` (the raw StringBuilder inside `ExecStreamingAsync`) as the authoritative source for `ParseResponse`.
2. **Harden `ClaudeRunner.ParseResponse`** against line-wrapped stream-json. Options:
   - Look for the final `{"type":"result"` substring and match balanced braces to extract the complete JSON object (ignoring injected `\r\n`).
   - Disable PTY wrapping in docker exec — pass `-T` / `--no-tty` and `COLUMNS=9999` in the exec environment, so Claude CLI emits un-wrapped JSON lines.
   - Buffer until the process exits, then `Regex.Matches` on `^\{.*"type":"result".*\}$` with `Singleline` disabled (won't help here because of the injected breaks — the first option is more robust).
3. **Add a unit test** feeding a known line-wrapped Claude stream-json payload into `ClaudeRunner.ParseResponse` and asserting `Output` starts with `## Rewritten Task`.
4. **Separately**, fix the port-6080 collision in `SimpleAgentWorkflow` — either don't spawn a second container when `ContainerId` is already supplied by a parent, or make the VNC port dynamic.

## Final verdict

- `AgentRequest.SessionId = null` fix — **present and correct**.
- `ResearchPromptAsync` code structure — correct per spec.
- `FullOrchestrateWorkflow` wiring — correct.
- **Actual research output delivered to the workflow — broken.** The enhanced prompt is a 196KB raw Claude stream-json dump, and the other three fields are empty. Downstream activities receive this blob as input. Quality rating: **1/5**.
