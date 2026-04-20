# Phase 0 → Phase 1: Kickoff checklist

**When:** after Phase 0 plan sign-off, before Phase 1 Day 1 begins.

**Purpose:** verify prerequisites and environment readiness for migration execution.

**Duration:** ~2 hours.

---

## Pre-flight verification

Before starting Phase 1, confirm every item below:

### Team

- [ ] Migration owner identified and has time committed for 2-3 weeks.
- [ ] Reviewers identified for PRs during migration.
- [ ] Oncall rotation notified of increased PR volume and feature-flag changes.
- [ ] Stakeholder (product / leadership) notified per `temporal.md` §MM.1.

### Read-through

Every engineer participating has read:
- [ ] `temporal.md` §1-5 (intent + architecture).
- [ ] `temporal.md` §22 (phased plan overview).
- [ ] `temporal.md` §25 (anti-patterns).
- [ ] `docs/phase-guides/README.md`.
- [ ] The day-1 guide for the day they'll start on.

### Tools

- [ ] Docker Desktop running (or Linux Docker engine).
- [ ] .NET 10 SDK installed (`dotnet --version` ≥ 10.0.100).
- [ ] PowerShell 7+ (Windows) or equivalent bash environment.
- [ ] Git configured with signed commits (per `temporal.md` §Y.7).
- [ ] IDE: VS Code with C# Dev Kit, or Rider.
- [ ] Claude Code installed (if using AI assist).
- [ ] Temporal CLI installed (or available via `docker exec`).

### Repo state

- [ ] Checked out `temporal` branch.
- [ ] `git status` clean or expected mods only.
- [ ] `dotnet build` succeeds on current state.
- [ ] `dotnet test` passes (Elsa-era tests).

### Infrastructure

- [ ] `docker-compose.yml` base stack runs (`./scripts/dev-up.ps1`).
- [ ] Can reach `http://localhost:5000`.
- [ ] Can spawn a test container via existing API (smoke test).
- [ ] Worker-env image built (`magicpai-env:latest` in `docker images`).

### Credentials

- [ ] Claude CLI authenticated: `claude auth status` shows valid session.
- [ ] `~/.claude.json` + `~/.claude/.credentials.json` exist and current.
- [ ] Test session in current (Elsa) MagicPAI succeeds end-to-end.

### Backups

- [ ] Current `magicpai` database has a recent backup (created per `./scripts/backup.ps1`
      or equivalent).
- [ ] Backup restore procedure tested in the last 30 days (see `temporal.md` Appendix LL).
- [ ] Git branch `master` is a safe fallback.

### Documentation

- [ ] `SCORECARD.md` exists and is empty (ready to fill during execution).
- [ ] `PATCHES.md` exists and is empty.
- [ ] `CHANGELOG.md` has Phase 0 entry.
- [ ] This repo's `README.md` references `temporal.md` so team can find it.

### Feature flags

- [ ] If deploying to a real environment during Phase 2: feature flag mechanism in place
      (see `temporal.md` Appendix CC).
- [ ] Roll-forward and roll-back procedures tested in staging.

### Communication

- [ ] `#magicpai-temporal` Slack channel created (or equivalent).
- [ ] Status page for migration updates (optional; useful for team-wide awareness).

### AI-assist (if using Claude Code)

- [ ] `.claude/settings.json` allows the required tools per `temporal.md` §OO.2.
- [ ] Memory configured per `temporal.md` §OO.1.
- [ ] Phase 1 prompt template ready (from §HH.2).

### Risk acceptance

- [ ] Operator has read `temporal.md` §22.7 (risk register) and §23 (rollback).
- [ ] Decision on what to do if Phase 1 reveals blocker: revert vs. push through.
- [ ] Accepted timeline: 10-14 days total; delays communicated to stakeholders.

### Budget / capacity

- [ ] Claude / Codex / Gemini token budget reviewed; not at risk of exhaustion during
      the migration window.
- [ ] Cloud compute reserved if running Temporal Cloud or cloud-hosted DB.
- [ ] Time on calendar blocked for migration days (no competing priorities).

---

## Sign-off

Signing below means you've verified every item above:

```
Tech lead:        _______________________  Date: _______
Migration owner:  _______________________  Date: _______
Release mgr:      _______________________  Date: _______
```

File this completed checklist at `docs/kickoff-YYYY-MM-DD.md`.

---

## Kickoff meeting agenda (30 min)

1. (5 min) Review this checklist; confirm all checked.
2. (5 min) Read `temporal.md` §1 together.
3. (5 min) Clarify roles (who's Day 1? Day 2-3? Day 4+? Phase 2 lead? Phase 3?).
4. (5 min) Agree on Slack channel, daily standup time, PR review cadence.
5. (5 min) Agree on exit criteria for each phase (§22.3, §22.4, §22.5).
6. (5 min) Any questions, concerns, risks.

Record: decisions in `docs/phase-kickoff-decisions.md`.

---

## Go / no-go decision

Before starting Day 1:
- All checklist items checked: GO.
- Any unchecked item: resolve it or postpone kickoff.

If uncertain: postpone. The plan is not going anywhere.

---

## Next

`Phase1-Day1.md` — infrastructure setup.
