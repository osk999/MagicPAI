# CLAUDE.md — MagicPAI

## Stack
- .NET 10, C# 13, Elsa Workflows 3.6.0, Blazor WASM, Docker, SignalR, xUnit + Moq
- Docker.DotNet for container management
- PostgreSQL (production) / SQLite (dev) via EF Core

## Build & Test
```bash
dotnet build
dotnet test
dotnet build MagicPAI.Core/MagicPAI.Core.csproj --no-restore   # fast single-project check
```

## Solution Structure
| Project | Type | Purpose |
|---|---|---|
| `MagicPAI.Core` | classlib | Shared models, interfaces, services (ClaudeRunner, gates, blackboard) |
| `MagicPAI.Activities` | classlib | Custom Elsa 3 activities (AI agents, Docker, verification, git) |
| `MagicPAI.Workflows` | classlib | Built-in workflow templates (WorkflowBase classes) |
| `MagicPAI.Server` | web | ASP.NET Core host (Elsa runtime + SignalR hub + REST API) |
| `MagicPAI.Studio` | blazorwasm | Blazor WASM frontend extending Elsa Studio |
| `MagicPAI.Tests` | xunit | Unit tests |

## Specification
**Read `MAGICPAI_PLAN.md`** for the complete project specification including architecture,
all activity definitions, code examples, Docker setup, and file manifest.

## Open Source Reference Policy
- For Elsa-related questions, bugs, behavior, APIs, Studio integration, activities, runtime flow, and debugging, check `document_refernce_opensource/` before relying on memory.
- Use these locations in order:
  0. `document_refernce_opensource/README.md` and `document_refernce_opensource/REFERENCE_INDEX.md` to find the right area fast
  1. `document_refernce_opensource/docs/` for expected framework behavior and concepts
  2. `document_refernce_opensource/elsa-core/` for runtime and server-side implementation details
  3. `document_refernce_opensource/elsa-studio/` for Studio and UI implementation details
- Treat `document_refernce_opensource/` as a local snapshot. If version drift may matter, say so explicitly.
- Read targeted files only. Do not load the entire reference tree into context.
- When explaining an issue, separate:
  - MagicPAI code behavior
  - Elsa upstream behavior
  - any inference made from reading source
- If there is a mismatch between MagicPAI and Elsa upstream docs or source, state that explicitly instead of guessing.
- Prefer citing the exact local file path from `document_refernce_opensource/` that supports the explanation.
- Default debugging order:
  1. inspect MagicPAI integration code
  2. verify expected behavior in `document_refernce_opensource/docs/`
  3. inspect `document_refernce_opensource/elsa-core/` or `document_refernce_opensource/elsa-studio/` if docs are incomplete

## Verify Against Reference (CRITICAL)
- **Every implementation change MUST be verified against `document_refernce_opensource/`.**
- Before committing any Elsa-related change (activities, workflows, JSON templates, Studio
  integration, runtime config), cross-check the actual API/behavior in the reference source.
- Do NOT rely on memory or assumptions about how Elsa works. The reference snapshot is
  the source of truth. Read the relevant file and confirm your approach matches.
- After making changes, verify:
  1. Activity input/output types match the reference definitions
  2. Workflow JSON schema matches `document_refernce_opensource/elsa-core/` serialization format
  3. Expression types (Literal, Variable, JavaScript) are used correctly per reference
  4. Connection port names match the `[FlowNode]` outcomes in the activity source
  5. Studio integration follows patterns in `document_refernce_opensource/elsa-studio/`
- If you find a discrepancy between your change and the reference, STOP and fix it
  before proceeding.

## Elsa Activity Rules (CRITICAL)
- Base class: `Activity` or `CodeActivity` from `Elsa.Workflows`
- Inputs: `public Input<T> Prop { get; set; }` with `[Input]` attribute
- Outputs: `public Output<T> Prop { get; set; }` with `[Output]` attribute
- Outcomes: `[FlowNode("Done", "Failed")]` attribute on the class
- Complete: `await context.CompleteActivityWithOutcomesAsync("Done")`
- DI: `context.GetRequiredService<IMyService>()` — NOT constructor injection
- Logging: `context.AddExecutionLogEntry("EventName", message)` — NOT Console.WriteLine
- Category: `[Activity("MagicPAI", "Category/Sub", "Description")]`

## C# Rules
- Always `await` async methods. Never `.Result` or `.Wait()`
- Use `DateTime.UtcNow`, never `DateTime.Now`
- Parameterized SQL only, never string concatenation
- No `using System.Linq;` — implicit usings are enabled
- Namespace pattern: `MagicPAI.{Project}.{Folder}`

## Interface Contracts (must be implemented exactly)
- `ICliAgentRunner` — BuildCommand(), ParseResponse(), AgentName, DefaultModel, AvailableModels
- `ICliAgentFactory` — Create(agentName), AvailableAgents
- `IContainerManager` — SpawnAsync(), ExecAsync(), ExecStreamingAsync(), DestroyAsync()
- `IVerificationGate` — Name, IsBlocking, CanVerifyAsync(), VerifyAsync()
- `IExecutionEnvironment` — RunCommandAsync(), StartProcessAsync(), Kind

## Operator Role (CRITICAL)
- **All code writing, testing, and verification MUST be done by MagicPAI workflows.**
- The human operator (Claude Code session) is a **monitor only** — track progress,
  take screenshots, verify visually, and fix MagicPAI bugs/infrastructure.
- **Never write output code directly.** Instead, create a MagicPAI session via
  `POST /api/sessions` with the appropriate prompt and workflow.
- If MagicPAI has bugs or infrastructure issues (Docker, DB, Elsa, etc.),
  fix those in the MagicPAI codebase, then restart the workflow.
- Use browser automation (Playwright/Chrome MCP) to visually verify results.

## File Ownership (for parallel agents)
- **core agent**: MagicPAI.Core/**
- **activities agent**: MagicPAI.Activities/**
- **server agent**: MagicPAI.Server/**, MagicPAI.Workflows/**
- **studio agent**: MagicPAI.Studio/**, docker/**
