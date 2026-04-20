# Chaos engineering results

Results of chaos experiments run against MagicPAI. See `temporal.md` §O.8.

Last updated: 2026-04-20 (pre-Phase-1; no results yet).

---

## Format

Each experiment logged as:

```markdown
## YYYY-MM-DD: [Scenario name]

- **Owner:** ___
- **Environment:** staging | prod
- **Duration of experiment:** ___ min
- **Injected fault:** what we did
- **Expected behavior:** what we thought would happen
- **Observed behavior:** what actually happened
- **Recovery time:** how long until normal
- **Blast radius:** how much broke
- **Findings:** what we learned
- **Follow-up:** action items
```

---

## Results

*(none yet — first chaos run scheduled for Phase 4)*

---

## Planned experiments (quarterly rotation)

| # | Scenario | Last run | Next scheduled |
|---|---|---|---|
| 1 | Kill worker mid-activity | — | Q2 2026 |
| 2 | Restart Temporal server briefly | — | Q2 2026 |
| 3 | Fill MagicPAI DB disk | — | Q2 2026 |
| 4 | Slow Docker daemon (spawn latency) | — | Q3 2026 |
| 5 | Partition Temporal ↔ worker network | — | Q3 2026 |

## Runbooks triggered

Note which runbook (`temporal.md` Appendix BBB) was used for each chaos
recovery:

*(populate as experiments run)*

## Safety

Chaos engineering against **staging only** unless explicitly approved for prod.

Prod chaos requires:
- Tech lead approval.
- Off-hours execution window.
- Immediate rollback plan.
- Pre-declared SEV-3 incident in PagerDuty (for team awareness).

## Automation

Phase 4+: consider tools like
- [Chaos Toolkit](https://chaostoolkit.org/) for scripted chaos.
- [Litmus](https://litmuschaos.io/) for Kubernetes-native chaos.
- Custom scripts in `deploy/chaos/*.sh`.

Not in scope for Phase 0-3.

## See also

- `temporal.md` §O.8 — chaos scenarios.
- `temporal.md` Appendix LL — DR rehearsals (more structured than chaos).
- Chaos vs DR rehearsal distinction: `temporal.md` §LL.9.
