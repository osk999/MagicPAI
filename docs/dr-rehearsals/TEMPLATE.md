# DR rehearsal: YYYY-MM-DD — [Scenario name]

**Scenario:** (one of: Temporal DB loss; MagicPAI DB loss; Full stack loss;
Worker fleet loss; Network partition; Docker daemon crash; Credential expiration;
CVE patch drill; other.)

See `temporal.md` Appendix LL for the scenario catalog.

---

## Metadata

- **Environment:** staging
- **Participants:**
  - IC: ___
  - SME: ___
  - Comms: ___
  - Scribe: ___
- **Duration:** ___ min
- **RTO target:** 30 min
- **RPO target:** 24 h

## Setup

(Describe how the scenario was simulated.)

```bash
# Example for Temporal DB loss:
docker compose -f docker-compose.staging.yml stop temporal temporal-db
docker volume rm magicpai-staging_temporal-pgdata
```

## Timeline (UTC)

- T+00:00 Simulated loss initiated.
- T+00:0X Alert fired.
- T+00:0X Oncall paged.
- T+00:0X Investigation started.
- T+00:XX Restore initiated.
- T+00:XX Cluster healthy.
- T+00:XX Smoke test passing.
- T+00:XX All-clear.

**Total recovery time: ___ min**

## Outcome

- [ ] RTO met (< 30 min)
- [ ] RPO met (no data loss beyond backup window)
- [ ] Smoke test passes post-restore
- [ ] Historical workflows queryable

## Issues discovered

(What broke that we didn't expect.)

1. ...

## Fixes applied during the rehearsal

(What needed hot-patching.)

1. ...

## Follow-up fixes

(What needs permanent fixing.)

| # | Action | Owner | Due |
|---|---|---|---|
| 1 | | | |

## Commands that worked

(For future reference.)

```bash
# ...
```

## Commands that didn't work

```bash
# ...
# Why: ...
# Fixed by: ...
```

## Runbook accuracy

Did the runbook (see `temporal.md` Appendix BBB) match reality?

- [ ] Yes — runbook is accurate.
- [ ] Mostly — with minor corrections (list):
- [ ] No — significant gaps (list):

Updates needed:

- ...

## Team readiness

- [ ] IC knew their role.
- [ ] SME located correct runbook.
- [ ] Comms knew when to update status page.
- [ ] Communication channels worked.

Training gaps found:

- ...

## Improvements for next rehearsal

- ...

## Next rehearsal

- **Date:** (typically +3 months)
- **Scenario:** (rotate through Appendix LL.2 list)
- **Participants:** (rotate)

## Sign-off

- SRE: ___
- Tech lead: ___

## Attachments

- Screenshot of Grafana during rehearsal: (link)
- Temporal UI snapshot post-restore: (link)
