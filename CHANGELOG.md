# Changelog

All notable changes to MagicPAI are documented here.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning: [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

_(No unreleased changes — 2.0.0 tag is pending user approval.)_

---

## [2.0.0] — 2026-04-20

Phases 1-3 of the Temporal migration complete, all in a single window.
Elsa Workflows 3.6 fully retired; MagicPAI now runs exclusively on Temporal.io 1.13.

### Added
- `Temporalio` 1.13.0 client + hosted worker wired into `MagicPAI.Server/Program.cs`.
- `MagicPAI.Activities` — 20 `[Activity]` methods across 5 domain classes
  (`AiActivities`, `DockerActivities`, `GitActivities`, `VerifyActivities`,
  `BlackboardActivities`).
- `MagicPAI.Workflows` — typed input/output contract records, shared
  `ActivityProfiles` (Short / Medium / Long / Container / Verify).
- 16 `[Workflow]` classes ported to Temporal semantics in `MagicPAI.Server.Workflows`:
  SimpleAgent, VerifyAndRepair, PromptEnhancer, ContextGatherer, PromptGrounding,
  OrchestrateSimplePath, ComplexTaskWorker, OrchestrateComplexPath,
  PostExecutionPipeline, ResearchPipeline, StandardOrchestrate, ClawEvalAgent,
  WebsiteAuditCore, WebsiteAuditLoop, FullOrchestrate, DeepResearchOrchestrate.
- `SignalRSessionStreamSink` + `ISessionStreamSink` for live CLI-output streaming
  from activities to the Studio.
- `SearchAttributesInitializer` + `WorkflowCompletionMonitor` +
  `DockerEnforcementValidator` hosted services.
- `ActivityProfiles.cs` — central timeout/retry policy per activity class.
- Studio rebuilt on MudBlazor: 8 new components (`SessionInputForm`,
  `CliOutputStream`, `CostDisplay`, `GateApprovalPanel`, `ContainerStatusPanel`,
  `VerificationResultsTable`, `SessionStatusBadge`, `PipelineStageChip`).
- New Studio pages: `Home`, `SessionList`, `SessionView` (rewritten),
  `SessionInspect`; `Settings` updated.
- `TemporalUiUrlBuilder`, `WorkflowCatalogClient` client-side services.
- Replay tests (17) with captured histories under
  `MagicPAI.Tests/Workflows/Histories/` — CI gate against workflow
  non-determinism.
- Smoke / backup / restore / determinism scripts under `scripts/` and `deploy/`.
- Docker compose overlay `docker/docker-compose.temporal.yml` + dynamic config
  under `docker/temporal/`.
- `PATCHES.md` — workflow patch log for `Workflow.Patched()` change IDs.
- `SCORECARD.md` — per-phase live tracker (now marked Phase 3 complete).
- `temporal.md` — canonical migration blueprint (~24k lines, 58 appendices).
- `docs/phase-guides/` — Phase0 through Phase3-Day14 step-by-step guides.

### Changed
- `MagicPAI.Server` host now orchestrates the Temporal client + hosted worker;
  the Elsa runtime and SQLite persistence layer are gone.
- `MagicPAI.Studio` is a custom MagicPAI UX; Elsa Studio integration removed.
- `SessionController` uses `TemporalClient` for dispatch, cancel, terminate;
  `SessionHub` signals Temporal for gate approve/reject and prompt injection.
- `SessionHistoryReader` uses `Client.ListWorkflowsAsync` instead of Elsa
  persistence.
- `WorkflowCatalog` + `SessionLaunchPlanner` rewritten for Temporal task-queue
  dispatch.
- Stack: .NET 10, C# 13, **Temporal.io 1.13**, Blazor WASM, Docker, SignalR.

### Removed
- `Elsa.*` NuGet packages from every `.csproj`.
- `using Elsa.*` from all source trees (Core / Activities / Workflows / Server
  / Studio / Tests — all verified zero hits).
- All workflow JSON templates (23 files under `MagicPAI.Server/Workflows/Templates/`
  and `MagicPAI.Workflows/Templates/`).
- Elsa-era activity classes (24 files) and workflow classes (9 Elsa-only
  workflows in `MagicPAI.Workflows/`).
- `WorkflowBase.cs`, `WorkflowBuilderVariableExtensions.cs`,
  `WorkflowInputHelper.cs`.
- `ElsaEventBridge.cs`, `WorkflowPublisher.cs`, `WorkflowCompletionHandler.cs`,
  `WorkflowProgressTracker.cs`, `MagicPaiActivityDescriptorModifier.cs`.
- Elsa-specific Studio services: `ElsaStudioApiKeyHandler`, `MagicPaiFeature`,
  `MagicPaiMenuProvider`, `MagicPaiMenuGroupProvider`,
  `MagicPaiWorkflowInstanceObserverFactory`, `WorkflowInstanceLiveUpdater`,
  `DummyAuthHandler`, `ElsaStudioView` page.
- Elsa reference snapshot directories
  (`document_refernce_opensource/elsa-core/` and `.../elsa-studio/`).
- `MagicPAI.Server/elsa.sqlite.db` (Elsa state DB).
- 142 files deleted in Phase 3 Day 13 overall.

### Tests
- Suite size: 508 Elsa-era tests -> **276 Temporal-era tests** (232 unit + 17
  replay + 22 integration + 5 UI). The absolute count drop is because
  Elsa-era designer / JSON-template / dual-runtime tests were removed; the new
  tests cover a superset of behavior under deterministic Temporal semantics.
- `dotnet build MagicPAI.slnx -c Release` — 0 errors.

### Documentation
- `CLAUDE.md` rewritten for Temporal-only stack (see `temporal.md` Appendix R).
- Reference folder migrated: Elsa snapshots removed, Temporal pointers added
  (sdk-dotnet + docs repos — clone on demand).
- `CHANGELOG.md` cut to 2.0.0 (this entry).

### Notes
- Migration executed on the `temporal` branch, now ready to merge.
- Tag `v2.0.0-temporal` is **not yet created** — pending user approval
  (see `SCORECARD.md` Phase 3 sign-off section).

---

## [2.0.0-phase0] — 2026-04-20

### Added
- `temporal.md` — canonical migration blueprint (~24 000 lines, 58 appendices).
- `TEMPORAL_MIGRATION_PLAN.md` — executive summary.
- `SCORECARD.md` — live migration progress tracker.
- `PATCHES.md` — workflow patch log (empty at start).
- Branch `temporal` — where migration work will happen.
- 3 parallel research agents produced comprehensive Temporal.io research,
  consolidated into the plan.

### Changed
- Nothing in production code. This is a planning-only release.

### Notes
- 15 Architecture Decision Records (ADRs) documented.
- Team sign-off on plan pending.
- Phase 1 execution begins after sign-off.

---

## [1.9.x] — pre-migration (Elsa 3.6)

Historical Elsa-based releases. See git tags for version history.

Notable issues that motivated the Temporal migration:
- Variable shadowing bug: `ExpressionExecutionContext.GetInput()` silently returned
  same-named variable instead of dispatch input.
- Dual JSON/C# workflow split: workflows with C# delegates couldn't export JSON,
  causing two classes of workflows with different behavior and test coverage.
- Elsa Studio designer instability: visual designer generated graphs that
  sometimes disagreed with runtime, broke on minor Elsa upgrades.

These issues are structurally resolved by the Temporal migration (2.0.0).

---

## Release types

- **major**: breaking API/behavior change or major migration (e.g., 2.0.0).
- **minor**: new workflow type, new feature, non-breaking.
- **patch**: bug fix, security patch.
- **phaseN**: incremental milestones within a major release window.

## Commit -> CHANGELOG conventions

- Every user-facing change: add an entry here.
- Internal refactors: optional; add if operators need to know.
- Phase completions during migration: add entry.
- When a version is cut: move "Unreleased" items to the new version section.

## Release tag format

- `v<semver>` — `v2.0.0`, `v2.0.1`, `v2.1.0`
- Phase-specific: `v2.0.0-phase1`, `v2.0.0-phase2`, `v2.0.0-temporal` (final)
- Pre-release: `v2.0.0-rc.1`, `v2.0.0-alpha.3`

## Links

- [Migration plan](temporal.md)
- [Executive summary](TEMPORAL_MIGRATION_PLAN.md)
- [Scorecard](SCORECARD.md)
- [Patch log](PATCHES.md)
- [AI rules](CLAUDE.md)
