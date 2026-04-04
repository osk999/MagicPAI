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

## File Ownership (for parallel agents)
- **core agent**: MagicPAI.Core/**
- **activities agent**: MagicPAI.Activities/**
- **server agent**: MagicPAI.Server/**, MagicPAI.Workflows/**
- **studio agent**: MagicPAI.Studio/**, docker/**
