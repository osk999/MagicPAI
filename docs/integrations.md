# External tool integrations

Inventory of every external system MagicPAI integrates with. Referenced from
`temporal.md` §CCC.18.

Last updated: 2026-04-20 (planning; actuals added as integrations go live).

---

## Active integrations

*(no production integrations yet — this starts populating in Phase 1+)*

---

## Planned integrations (Phase 1+)

| Integration | Purpose | Owner | Plan section | Status |
|---|---|---|---|---|
| Docker daemon | Container management | Platform | §11, §13 | existing |
| PostgreSQL | App + Temporal state | Ops | §12, §13 | existing |
| Temporal server (self-hosted) | Workflow engine | Platform | §4, §13 | Phase 1 |
| Claude CLI | AI agent | Platform | §3.3 | existing |
| Codex CLI | AI agent (alt) | Platform | §3.3 | existing |
| Gemini CLI | AI agent (alt) | Platform | §3.3 | existing |

## Future (optional, not Phase 1-3 scope)

| Integration | Purpose | Justification | Decision |
|---|---|---|---|
| Prometheus | Metrics | Production monitoring | deferred (Phase 4) |
| Grafana | Dashboards | Visibility | deferred (Phase 4) |
| Alertmanager | Alerting | Incident response | deferred (Phase 4) |
| PagerDuty | Oncall | Incident routing | deferred (Phase 4) |
| Slack | Team comms | Notifications | existing internal |
| Datadog (alt to Prom+Grafana) | Observability | SaaS convenience | alternative to Prom |
| Sentry | Error tracking | Frontend errors | TBD |
| LaunchDarkly | Feature flags | Targeting | optional (we use appsettings) |
| GitHub Copilot | Dev tooling | Accelerate dev | existing individual |
| Claude Code | AI dev collab | Migration execution | **active** |

---

## Integration metadata template

For each integration when it goes live:

```markdown
### <Integration name>

- **Purpose:** <one line>
- **Owner:** <team/role>
- **Contract/account:** <link to management UI or account number>
- **Cost:** <monthly estimate>
- **Criticality:** <critical | important | nice-to-have>
- **Failure impact:** <what breaks if this goes down>
- **Configuration:** <where config lives — env vars, file paths>
- **Secrets:** <reference to §KK.1 entry>
- **Runbook:** <link to runbook for common ops>
- **Documentation:** <vendor docs or internal guide>
```

## Review cadence

Per `temporal.md` §CCC.18:
- **Quarterly:** full review — still using? still worth the cost? any unused?
- **Per-renewal:** decide renew vs switch vs drop.

## Consolidated cost tracking

Update monthly; cross-ref `temporal.md` §FF.9:

| Month | Total integration cost | Notes |
|---|---|---|
| 2026-04 | (planning — $0 production) | |

## Adding a new integration

Process (from `temporal.md` §CCC.19):

1. PR adds a row to this file.
2. ADR if architecturally significant (`temporal.md` Appendix N).
3. Update `temporal.md` §KK.1 if it requires a new secret.
4. Update `temporal.md` §CC if behind a feature flag.
5. Update `temporal.md` §FF.9 when cost visible.
6. Add runbook to `docs/runbooks/` if operationally meaningful.

## Removing an integration

1. PR that removes the integration code.
2. Update this file: move row from "Active" to "Removed" section.
3. Close vendor account if paid.
4. Revoke any secrets.

## Removed integrations

*(empty — none removed yet)*
