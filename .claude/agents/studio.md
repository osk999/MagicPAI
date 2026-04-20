---
name: studio
description: Build MagicPAI.Studio (Blazor WASM on MudBlazor) and Docker/Temporal infrastructure
isolation: worktree
---

You are working on **MagicPAI.Studio** (Blazor WASM frontend on MudBlazor — **not** Elsa Studio) and **docker/** (container images, docker-compose, Temporal stack).

## Your scope (ONLY touch these files)
- `MagicPAI.Studio/**`
- `MagicPAI.Shared/Hubs/**` (shared SignalR hub contracts with Server)
- `docker/**`
- `MagicPAI.Tests.UI/**` (bUnit component tests)

## Prerequisites
- `MagicPAI.Shared/Hubs/ISessionHubClient.cs` + `HubPayloads.cs` must exist (they do; both define the SignalR event surface).
- Read `CLAUDE.md` and `temporal.md` §10 + Appendix S before making any UI change.

## What lives here

### MagicPAI.Studio/
- `MagicPAI.Studio.csproj` — Blazor WASM. Packages: `MudBlazor 7.15.0`, `Microsoft.AspNetCore.SignalR.Client`, `Microsoft.AspNetCore.Components.WebAssembly[.DevServer]`. **No `Elsa.Studio.*`**.
- `Program.cs` — minimal MudBlazor host (~30 lines). `AddMudServices()`, scoped `HttpClient`, scoped `SessionApiClient` / `SessionHubClient` / `WorkflowCatalogClient` / `TemporalUiUrlBuilder`, scoped `BackendUrlResolver`.
- `App.razor` — pure MudBlazor Router; no Elsa Shell.
- `_Imports.razor` — `@using MudBlazor`, `@using MagicPAI.Studio.Components`, `@using MagicPAI.Studio.Services`, `@using MagicPAI.Shared.Hubs`. **No `@using Elsa.*`**.
- `Layout/MainLayout.razor` + `Layout/NavMenu.razor` — MudLayout shell.
- `Components/` — 8 MudBlazor components:
  - `SessionInputForm.razor` — workflow-type dropdown + prompt + assistant/model/workspace + submit.
  - `CliOutputStream.razor` — live stdout pane (MudBlazor dark monospace, auto-scroll).
  - `CostDisplay.razor` — subscribes to `SessionHubClient.CostUpdate`.
  - `GateApprovalPanel.razor` — approve/reject buttons dispatching Temporal signals.
  - `ContainerStatusPanel.razor` — subscribes to `ContainerSpawned`/`ContainerDestroyed`.
  - `VerificationResultsTable.razor` — gate results table.
  - `SessionStatusBadge.razor` — status chip.
  - `PipelineStageChip.razor` — stage chip.
- `Pages/` — `Home.razor`, `SessionList.razor`, `SessionView.razor`, `SessionInspect.razor` (iframes Temporal UI), `Dashboard.razor`, `CostDashboard.razor`, `Settings.razor`.
- `Services/` —
  - `BackendUrlResolver.cs` — resolves `/api` URL from config.
  - `SessionApiClient.cs` — `POST /api/sessions`, `GET /api/sessions`, `DELETE /api/sessions/{id}`, signal/approve/terminate endpoints.
  - `SessionHubClient.cs` — SignalR client matching `ISessionHubClient` — all events (`OutputChunk`, `StructuredEvent`, `StageChanged`, `CostUpdate`, `VerificationResult`, `GateAwaiting`, `ContainerSpawned`, `ContainerDestroyed`, `SessionCompleted`, `SessionFailed`, `SessionCancelled`).
  - `TemporalUiUrlBuilder.cs` — deep-link to `http://localhost:8233/namespaces/magicpai/workflows/{id}`.
  - `WorkflowCatalogClient.cs` — fetches `GET /api/workflows`.

### docker/
- `docker-compose.yml` — base: `server`, `db` (magicpai Postgres), `worker-env-builder` (profile `build`).
- `docker-compose.temporal.yml` — overlay: `temporal` (auto-setup 1.25), `temporal-db` (Postgres), `temporal-ui` (2.30). Use together with base.
- `temporal/dynamicconfig/development.yaml` + `production.yaml` — Temporal dynamic config.
- `temporal/README.md` — explains the Temporal config.
- `worker-env/Dockerfile` + `entrypoint.sh` — session container image (`magicpai-env:latest`) with Claude/Codex/Gemini CLIs + credential mounting. **Unchanged by the Temporal migration.**
- `server/Dockerfile` — MagicPAI.Server container image. Publishes Blazor WASM static files into server's wwwroot.

## Specifications

- **Plan:** `temporal.md` §10 (Studio migration), §S (Blazor components full code), §13 (Docker infrastructure).
- **Day-by-day guide:** `docs/phase-guides/Phase2-Day11.md`.

## Key patterns

- MudBlazor components only — no custom Bootstrap/Radzen/Fluent/BlazorMonaco.
- Every hub event subscription must unsubscribe on Dispose (`IAsyncDisposable` or `IDisposable`).
- `TemporalUiUrlBuilder.InitializeAsync()` fetches `/api/config/temporal` with a silent fallback to defaults — don't fail the UI if the server doesn't expose that endpoint.
- Studio is pure client; don't embed server logic. Everything goes through `SessionApiClient` or `SessionHubClient`.

## Docker invariant

Session containers (`magicpai-env`) ALWAYS run in Docker. The compose overlay `docker-compose.temporal.yml` adds the Temporal stack but does NOT change the session-container model.

## Don't do

- Don't re-introduce `Elsa.Studio.*` packages.
- Don't embed iframe to Elsa Studio (it's gone).
- Don't put any server logic in the Studio project.
- Don't route large CLI stdout through anything but SignalR `OutputChunk`.
- Don't modify `MagicPAI.Core` or `MagicPAI.Server`.

## After changes

1. `dotnet build MagicPAI.Studio/MagicPAI.Studio.csproj` — 0 errors.
2. `dotnet test MagicPAI.Tests.UI` — 5+ tests pass.
3. `dotnet publish MagicPAI.Studio -c Release` — verify `wwwroot/_content/` contains only `MudBlazor/` (no Elsa assets).
4. Start server + open `http://localhost:5000` to manually verify the Blazor shell boots and the workflow dropdown is populated.
5. Update `SCORECARD.md` Phase 2 Studio rebuild section.
