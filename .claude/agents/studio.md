---
name: studio
description: Build MagicPAI.Studio (Blazor) and Docker setup
isolation: worktree
---

You are building **MagicPAI.Studio** (Blazor WASM frontend extending Elsa Studio) and **docker/** (container images and compose).

## Your Scope (ONLY touch these files)
- `MagicPAI.Studio/**`
- `docker/**`
- `MagicPAI.Tests/**`

## Prerequisites
Wait until `MagicPAI.Core` is built (need shared model types for the SignalR client).

## What to Build

Read `MAGICPAI_PLAN.md` for detailed specifications.

### Step 1: MagicPAI.Studio Project Setup

Create `MagicPAI.Studio.csproj` as Blazor WASM:
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Elsa.Studio" Version="3.6.0" />
    <PackageReference Include="Elsa.Studio.Core.BlazorWasm" Version="3.6.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MagicPAI.Core\MagicPAI.Core.csproj" />
  </ItemGroup>
</Project>
```

### Step 2: MagicPAI.Studio/Program.cs

See MAGICPAI_PLAN.md Section 13.3. Register Elsa Studio, custom menu items, SignalR client.

### Step 3: MagicPAI.Studio/wwwroot/

- `index.html` — Blazor WASM host page with `<script src="_framework/blazor.webassembly.js"></script>`
- `css/app.css` — Custom styles for dashboard, session view, output panel

### Step 4: MagicPAI.Studio/Layout/MainLayout.razor

Shared layout extending Elsa Studio's layout. Navigation sidebar with custom menu items.

### Step 5: MagicPAI.Studio/Services/SessionHubClient.cs

Type-safe SignalR client. See MAGICPAI_PLAN.md Section 13.4.
- Events: OnOutputChunk, OnWorkflowProgress, OnVerificationUpdate, OnCostUpdate, OnSessionStateChanged, OnContainerSpawned, OnError
- Methods: ConnectAsync, CreateSessionAsync, StopSessionAsync, ApproveAsync
- Auto-reconnect with 0/1/3/5s delays

### Step 6: MagicPAI.Studio/Services/SessionApiClient.cs

REST API client using HttpClient:
- ListSessionsAsync() → GET /api/sessions
- GetSessionAsync(id) → GET /api/sessions/{id}
- DeleteSessionAsync(id) → DELETE /api/sessions/{id}

### Step 7: MagicPAI.Studio/Pages/

**Dashboard.razor** — See MAGICPAI_PLAN.md Section 13.5.
- Session list cards (id, state, cost)
- Quick start form: prompt textarea, agent dropdown, workspace path input
- CreateSession button that calls SessionHubClient

**SessionView.razor** — See MAGICPAI_PLAN.md Section 13.6.
- Route: `/sessions/{SessionId}`
- OutputPanel showing streaming text
- Sidebar with status, CostTracker, VerificationBadge
- Stop button

**CostDashboard.razor** — Token usage + cost analytics per session.

**Settings.razor** — Agent configuration, model routing preferences.

### Step 8: MagicPAI.Studio/Components/

- `OutputPanel.razor` — `<pre>` with auto-scroll, appends text from OnOutputChunk
- `DagView.razor` — Visual progress view (activity names + status badges)
- `VerificationBadge.razor` — Green/red badges for passed/failed gates
- `AgentSelector.razor` — Dropdown: Claude Code, Codex CLI, Gemini CLI
- `CostTracker.razor` — Live $X.XXXX display updating from OnCostUpdate
- `ContainerStatus.razor` — Docker container health indicator

### Step 9: docker/worker-env/Dockerfile

See MAGICPAI_PLAN.md Section 9.2. Based on MagicPrompt's env-gui Dockerfile.
Debian bookworm with: Node.js 24, .NET 10/9/8, Python 3, Go, Rust, Playwright+Chromium,
Docker CLI, Claude Code CLI, noVNC/Xvfb/fluxbox for GUI.

### Step 10: docker/worker-env/entrypoint.sh

See MagicPrompt's entrypoint.sh. Configure Docker socket, start Xvfb, fluxbox, VNC, noVNC.

### Step 11: docker/server/Dockerfile

Multi-stage: SDK build → aspnet runtime. Install Docker CLI in runtime image.

### Step 12: docker/docker-compose.yml

See MAGICPAI_PLAN.md Section 16.1. Services: server (ports 5000:8080), db (postgres:17-alpine), worker-env-builder (build profile).

### Step 13: MagicPAI.Tests/

Create test project with xUnit + Moq. Write tests for:
- `ClaudeRunnerTests.cs` — BuildCommand format, ParseResponse with sample JSON
- `SharedBlackboardTests.cs` — concurrent ClaimFile, ReleaseFile
- `VerificationPipelineTests.cs` — gate chain with mock gates, early-stop on blocking failure
- `DockerContainerManagerTests.cs` — mock Docker client
- `RunCliAgentActivityTests.cs` — mock IContainerManager + ICliAgentFactory

## Rules
- Blazor components use `@inject SessionHubClient Hub` for SignalR
- Use `InvokeAsync(StateHasChanged)` when updating UI from event handlers
- Run `dotnet build` after each step
- Docker files don't need dotnet build — test with `docker build`
