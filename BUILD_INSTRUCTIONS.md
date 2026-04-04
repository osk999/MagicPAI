# How to Launch the MagicPAI Build

## Quick Start (Agent Teams)

### 1. Open Claude Code in the MagicPAI directory

```bash
cd C:\AllGit\CSharp\MagicPAI
claude
```

### 2. Paste this prompt to start building

```
Read MAGICPAI_PLAN.md — this is the complete specification for the MagicPAI project.

Build the entire project using agent teams. Here's the plan:

PHASE 1 — SCAFFOLD (you do this directly, don't delegate):
1. Create MagicPAI.sln
2. Create all 6 .csproj files with correct NuGet package references
3. Run `dotnet restore` and `dotnet build` to verify the skeleton compiles

PHASE 2 — PARALLEL BUILD (spawn 4 teammates):
Use the subagent definitions in .claude/agents/ to spawn 4 teammates:

Teammate "core":
  → Builds MagicPAI.Core (models, interfaces, services, gates, config)
  → MUST complete first — other teammates depend on its interfaces

Teammate "activities" (after core completes):
  → Builds MagicPAI.Activities (all Elsa custom activities)

Teammate "server" (after core + activities complete):
  → Builds MagicPAI.Server (Program.cs, SessionHub, EventBridge, Controllers)
  → Builds MagicPAI.Workflows (FullOrchestrate, SimpleAgent, VerifyRepair)

Teammate "studio" (after core completes):
  → Builds MagicPAI.Studio (Blazor pages, components, SignalR client)
  → Builds docker/ (Dockerfiles, compose)
  → Builds MagicPAI.Tests (unit tests)

PHASE 3 — INTEGRATION (you do this directly):
1. Run `dotnet build` for the full solution
2. Fix any cross-project reference errors
3. Run `dotnet test`
4. Verify Elsa activities are registered correctly

Each teammate should:
- Read MAGICPAI_PLAN.md for their section
- Read CLAUDE.md for conventions
- Run `dotnet build` after each step
- Only touch files in their assigned scope
```

### 3. Alternative: Run teammates manually (if agent teams not available)

If experimental agent teams aren't enabled, run 4 separate Claude Code sessions:

**Terminal 1 (core — run FIRST):**
```bash
cd C:\AllGit\CSharp\MagicPAI
claude -p "You are the 'core' agent. Read .claude/agents/core.md for your instructions. Read MAGICPAI_PLAN.md for specifications. Build everything described in your agent file. Run dotnet build after each step." --allowedTools Read,Write,Edit,Bash,Glob,Grep
```

**Terminal 2 (activities — run AFTER core completes):**
```bash
cd C:\AllGit\CSharp\MagicPAI
claude -p "You are the 'activities' agent. Read .claude/agents/activities.md for your instructions. Read MAGICPAI_PLAN.md for specifications. Build everything described in your agent file." --allowedTools Read,Write,Edit,Bash,Glob,Grep
```

**Terminal 3 (server — run AFTER core + activities):**
```bash
cd C:\AllGit\CSharp\MagicPAI
claude -p "You are the 'server' agent. Read .claude/agents/server.md for your instructions. Read MAGICPAI_PLAN.md for specifications. Build everything described in your agent file." --allowedTools Read,Write,Edit,Bash,Glob,Grep
```

**Terminal 4 (studio — run AFTER core):**
```bash
cd C:\AllGit\CSharp\MagicPAI
claude -p "You are the 'studio' agent. Read .claude/agents/studio.md for your instructions. Read MAGICPAI_PLAN.md for specifications. Build everything described in your agent file." --allowedTools Read,Write,Edit,Bash,Glob,Grep
```

### 4. Dependency Order

```
Phase 1: Scaffold (.sln + .csproj files)
    ↓
Phase 2a: core agent (MagicPAI.Core — models, interfaces, services)
    ↓ (must complete before activities/server start)
    ├── Phase 2b: activities agent (MagicPAI.Activities — Elsa activities)
    │       ↓ (must complete before server starts workflows)
    │       └── Phase 2c: server agent (MagicPAI.Server + MagicPAI.Workflows)
    │
    └── Phase 2d: studio agent (MagicPAI.Studio + docker/ + tests)
              (can run in parallel with activities)
    ↓
Phase 3: Integration (dotnet build full solution, fix errors)
```
