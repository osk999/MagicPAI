# Upgrade log

Living history of dependency upgrades. Referenced from `temporal.md` §II.12.

Last updated: 2026-04-20.

---

## Format

```markdown
### YYYY-MM-DD: <Component> <old> → <new>

- Release notes: <link>
- Breaking changes: <none | list>
- Tested: <staging duration, CI, etc.>
- Deployed: <date or planned>
- Owner: <person>
```

---

## Planned upgrades

*(nothing currently planned beyond the Phase 0 initial pinning)*

---

## History

### 2026-04-20: Phase 0 initial pinning

- `.NET SDK` pinned to 10.0.100 (`global.json`).
- `Temporalio` pinned to 1.13.0 (per Appendix A of `temporal.md`).
- `Temporalio.Extensions.Hosting` pinned to 1.13.0.
- `MudBlazor` pinned to 7.15.0.
- `postgres` pinned to 17-alpine.
- `temporalio/auto-setup` pinned to 1.25.0.
- `temporalio/ui` pinned to 2.30.0.
- Full version table in `temporal.md` Appendix C.12.
- No code changes yet; this documents the target state for Phase 1.
- Owner: Migration lead.

---

## Upcoming quarterly review

Q2 2026 (July): review all pins; upgrade any that has been released.

---

## Review cadence

Per `temporal.md` Appendix II.11:

| Component | Cadence | Next review |
|---|---|---|
| Temporalio SDK | Quarterly | 2026-07 |
| Temporal server | Quarterly | 2026-07 |
| .NET SDK minor | Monthly | 2026-05 |
| .NET SDK major | On LTS transition | — |
| PostgreSQL minor | Quarterly | 2026-07 |
| PostgreSQL major | Annually | 2027-04 |
| MudBlazor | Quarterly | 2026-07 |
| Docker base images | Monthly | 2026-05 |

---

## Triggered upgrades (out-of-cycle)

Log any upgrade done out of normal cadence and the reason:

*(none yet)*

---

## Security patches

Log CVE-triggered upgrades separately:

*(none yet)*

---

## See also

- `temporal.md` Appendix II — SDK upgrade guide (step-by-step procedures).
- `temporal.md` Appendix C.12 — version pin reference.
- `CHANGELOG.md` — user-facing release notes (not infrastructure upgrades).
