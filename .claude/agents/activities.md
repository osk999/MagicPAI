---
name: activities
description: Build MagicPAI.Activities — all custom Elsa 3 activities
isolation: worktree
---

You are building the **MagicPAI.Activities** project — custom Elsa 3 activities.

## Your Scope (ONLY touch these files)
- `MagicPAI.Activities/**`

## Prerequisites
Wait until `MagicPAI.Core` is built (interfaces and models must exist).
Read files in `MagicPAI.Core/Services/` and `MagicPAI.Core/Models/` to understand the contracts.

## What to Build

Read `MAGICPAI_PLAN.md` for detailed code examples. Build in this order:

### Step 1: AI Agent Activities (MagicPAI.Activities/AI/)

**RunCliAgentActivity.cs** — The core activity. See MAGICPAI_PLAN.md Section 6.2.
```
[Activity("MagicPAI", "AI Agents", "Execute a prompt via an AI CLI agent")]
[FlowNode("Done", "Failed")]
```
- Inputs: Agent (dropdown: claude/codex/gemini), Prompt (multiline), ContainerId, WorkingDirectory, Model, MaxTurns, TimeoutMinutes
- Outputs: Response, Success, CostUsd, FilesModified, ExitCode
- Uses IContainerManager + ICliAgentFactory from DI

**TriageActivity.cs** — Classify prompt complexity. See MAGICPAI_PLAN.md Section 10.4.
```
[FlowNode("Simple", "Complex")]
```
- Input: Prompt, ContainerId
- Output: Complexity (int), Category, RecommendedModel
- Uses Haiku model for cheap triage

**ArchitectActivity.cs** — Task decomposition. See MAGICPAI_PLAN.md Section 6.
- Input: Prompt, ContainerId, GapContext
- Output: TaskListJson (string[]), TaskCount

**ModelRouterActivity.cs** — Model selection. See MAGICPAI_PLAN.md Section 15.1.
- Input: TaskCategory, Complexity, PreferredAgent
- Output: SelectedAgent, SelectedModel

### Step 2: Docker Activities (MagicPAI.Activities/Docker/)

**SpawnContainerActivity.cs** — See MAGICPAI_PLAN.md Section 6.3.
- Inputs: Image, WorkspacePath, MemoryLimitMb, EnableGui, EnvVars (JsonEditor)
- Outputs: ContainerId, GuiUrl

**ExecInContainerActivity.cs** — Run shell command in container.
- Inputs: ContainerId, Command, WorkingDirectory
- Outputs: Output, ExitCode

**StreamFromContainerActivity.cs** — Stream real-time output from container.
- Inputs: ContainerId, Command
- Output: FullOutput

**DestroyContainerActivity.cs** — Cleanup container.
- Input: ContainerId

### Step 3: Verification Activities (MagicPAI.Activities/Verification/)

**RunVerificationActivity.cs** — See MAGICPAI_PLAN.md Section 6.4.
```
[FlowNode("Passed", "Failed", "Inconclusive")]
```
- Inputs: ContainerId, WorkingDirectory, Gates (CheckList), WorkerOutput
- Outputs: AllPassed, FailedGates (string[]), GateResultsJson

**RepairActivity.cs** — Generate AI repair prompt from failed gates.
- Inputs: ContainerId, FailedGates, OriginalPrompt, GateResultsJson
- Output: RepairPrompt

### Step 4: Git Activities (MagicPAI.Activities/Git/)

**CreateWorktreeActivity.cs**
- Inputs: ContainerId, BranchName
- Output: WorktreePath

**MergeWorktreeActivity.cs**
- Inputs: ContainerId, BranchName
- Output: Success, ConflictFiles

**CleanupWorktreeActivity.cs**
- Input: ContainerId, WorktreePath

### Step 5: Infrastructure Activities (MagicPAI.Activities/Infrastructure/)

**EmitOutputChunkActivity.cs** — Log output chunk for SignalR bridge.
**UpdateCostActivity.cs** — Track token costs.
**ClaimFileActivity.cs** — Atomic file ownership via SharedBlackboard.
**HumanApprovalActivity.cs** — Bookmark-based approval gate. See MAGICPAI_PLAN.md Section 15.3.

## Elsa Activity Pattern (follow exactly)
```csharp
[Activity("MagicPAI", "Category", "Description")]
[FlowNode("Done", "Failed")]
public class MyActivity : Activity
{
    [Input(DisplayName = "My Input")] 
    public Input<string> MyInput { get; set; } = default!;
    
    [Output(DisplayName = "My Output")]
    public Output<string> MyOutput { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var service = context.GetRequiredService<IMyService>();
        // ... logic ...
        MyOutput.Set(context, result);
        await context.CompleteActivityWithOutcomesAsync("Done");
    }
}
```

## Rules
- NEVER use constructor injection in activities — use context.GetRequiredService<T>()
- ALWAYS add [Activity] and [FlowNode] attributes
- Run `dotnet build MagicPAI.Activities/MagicPAI.Activities.csproj` after each step
