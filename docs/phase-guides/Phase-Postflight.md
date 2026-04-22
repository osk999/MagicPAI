# Post-migration: Phase 3+ ongoing ops

**When:** after `v2.0.0-temporal` tag created.

**Purpose:** sustained operations once the migration is complete.

---

## Week 1 post-migration

### Day +1 through +7

Daily:
- [ ] Monitor Grafana MagicPAI Overview dashboard. Any anomalies?
- [ ] Review overnight sessions for unexpected failures.
- [ ] Check container GC — any orphans?
- [ ] Review oncall pages — any Temporal-related?

Weekly:
- [ ] Attend team retrospective (scheduled per `temporal.md` §DD.6).
- [ ] Capture lessons in `docs/retro-temporal-migration.md`.
- [ ] Address urgent follow-up fixes from retro.

## Month 1

- [ ] Run first full DR rehearsal (per `temporal.md` Appendix LL).
- [ ] Baseline SLO attainment measured (per §JJ.11).
- [ ] Cost report for the month (§FF.9).
- [ ] Quarterly upgrade check for dependencies (§II.11).
- [ ] Verify pre-commit hooks still effective (no Elsa imports sneaking back).

## Quarter 1 post-migration

- [ ] Revisit ADRs (Appendix N) — any need revising?
- [ ] Review patches in `PATCHES.md` — any deprecations ready?
- [ ] Full chaos engineering suite (§O.8).
- [ ] Upgrade log review (`docs/upgrade-log.md`).
- [ ] Team training refresh (Appendix P).

## Year 1

- [ ] Full security audit (§17.15).
- [ ] Revisit `temporal.md` — prune obsolete sections.
- [ ] Measure success criteria achievement (§Q.14).
- [ ] Plan next major initiative (multi-region? Worker Versioning? new features?).

---

## Sustained practices

### Daily
- PR reviews with discipline on determinism grep + replay fixtures.
- Monitor alerts; triage within SLA.

### Weekly
- Review vulnerable dependencies (`dotnet list package --vulnerable`).
- Review failed workflows; are any patterns emerging?

### Monthly
- Run `./deploy/backup.sh` validation (restore to scratch).
- Review cost per workflow type in Grafana.
- Prune `session_events` older than 30 days.

### Quarterly
- Full dependency upgrade window.
- DR rehearsal (one scenario per quarter).
- SLO attainment review.
- ADR review.

### Annually
- Security audit.
- Major version upgrades if available.
- Temporal server minor upgrade cycle.
- Team training refresh.

---

## Drift detection

Over time, the codebase will diverge from `temporal.md`. Quarterly:

```bash
# Random spot-check: pick 5 items from temporal.md
# Verify each against current code
# File issues for any drift found
```

## When to retire temporal.md

After Phase 3 + 1 full quarter of stable operations:

Option A: keep it as historical reference in `docs/archive/temporal-migration-2026.md`.
Option B: prune to a short "why we moved to Temporal" section inside `MAGICPAI_PLAN.md`.

Decision: owner. Recommendation: Option B after ~6 months.

---

## Renewal of commitments

Annually, confirm:
- [ ] Still using Temporal (vs. consider alternatives)
- [ ] Still self-hosting (vs. consider Temporal Cloud)
- [ ] Still on current versions (vs. schedule major upgrade)
- [ ] ADRs still valid (vs. need updates)

---

## Links

- Operations runbook: `temporal.md` §19.
- Temporal CLI: `temporal.md` Appendix V.
- Error glossary: `temporal.md` Appendix W.
- Debugging recipes: `temporal.md` Appendix Z.
- Post-migration maintenance: `temporal.md` Appendix Q.
