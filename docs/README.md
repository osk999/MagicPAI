# MagicPAI — docs/

Auxiliary documentation. The canonical project docs live at the repo root:
- [`README.md`](../README.md) — project intro
- [`CLAUDE.md`](../CLAUDE.md) — AI-facing rules
- [`MAGICPAI_PLAN.md`](../MAGICPAI_PLAN.md) — architecture
- [`temporal.md`](../temporal.md) — Elsa→Temporal migration plan
- [`TEMPORAL_MIGRATION_PLAN.md`](../TEMPORAL_MIGRATION_PLAN.md) — migration summary
- [`SCORECARD.md`](../SCORECARD.md) — live migration progress
- [`PATCHES.md`](../PATCHES.md) — workflow patch log
- [`CHANGELOG.md`](../CHANGELOG.md) — release notes

## Files in this directory

| File | Purpose | Owner | Auto-generated? |
|---|---|---|---|
| `openapi.yaml` | OpenAPI 3.1 spec for REST API | build | yes (Swashbuckle) |
| `postman-collection.json` | Postman collection for API | dev | yes (from OpenAPI) |
| `accessibility.md` | WCAG 2.2 statement (`temporal.md` §WW) | tech lead | no |
| `agent-session-log.md` | Claude Code session history | migration owner | no |
| `claude-code-sessions.md` | Detailed Claude Code session notes | migration owner | no |
| `dr-rehearsals/YYYY-MM-DD.md` | DR rehearsal reports (§LL) | SRE | no |
| `retro-temporal-migration.md` | Post-migration retrospective (§DD.6) | tech lead | no |
| `upgrade-log.md` | Dependency upgrade history (§II.12) | ops | no |
| `integrations.md` | External tool inventory (§CCC.18) | ops | no |
| `chaos-results.md` | Chaos engineering results (§O.8) | SRE | no |
| `release-notes/` | Per-release notes templates | release mgr | no |

## Adding new documentation

1. PR that adds the file.
2. Update this `docs/README.md` index.
3. If user-facing: also update root `README.md` links section.
4. If Claude-facing: also update `CLAUDE.md` or relevant memory file.

## Documentation maintenance

Quarterly:
- Audit for drift (§DDD.6 in `temporal.md`).
- Prune obsolete docs or move to `docs/archive/`.
- Update auto-generated specs (OpenAPI).

## Conventions

- Kebab-case filenames: `retro-temporal-migration.md`, not `RetroTemporalMigration.md`.
- Date prefixes for time-specific: `2026-04-20-incident.md`.
- Link heavily: docs should cross-reference, not re-explain.
- Prefer short focused docs over kitchen-sink ones.

## Live vs archived

If a doc is still actively referenced and updated: here.
If a doc is historical reference but no longer changes: `docs/archive/`.

Example: after the Temporal migration completes, `temporal.md` likely gets
archived as `docs/archive/temporal-migration-2026.md`; `MAGICPAI_PLAN.md`
becomes the sole architecture reference.

## Questions?

- Architecture questions: start with `MAGICPAI_PLAN.md`.
- Migration questions: start with `temporal.md`.
- Operations questions: start with `temporal.md` §19 + Appendix V.
- AI-assisted development: start with `CLAUDE.md`.
