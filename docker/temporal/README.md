# docker/temporal/

Configuration files for the self-hosted Temporal stack.

## Contents

| Path | Purpose |
|---|---|
| `dynamicconfig/development.yaml` | Dev/local dynamic config — see `temporal.md` §13.4 |
| `dynamicconfig/production.yaml` | Production dynamic config — see `temporal.md` §13.5 |
| `certs/` (gitignored) | TLS certs for mTLS; generated per `temporal.md` §17.3 |
| `ui-config.yml` (prod only) | Temporal UI auth config — see `temporal.md` §14.7 |

## Usage

The `dynamicconfig/*.yaml` file is mounted into the `temporal` container at
`/etc/temporal/config/dynamicconfig/`. Temporal reloads it automatically every 10
seconds — safe to edit at runtime.

The compose overlay in `../docker-compose.temporal.yml` references this directory.

## Editing dynamic config

1. Edit the YAML file.
2. Save.
3. Wait ≤ 10s — Temporal picks up the change.
4. Verify via logs or by triggering the relevant behavior.

## Certs

For TLS-enabled setups:
1. Generate certs via `certs/generate.sh` (gitignored by default).
2. Mount into server + client via compose.
3. Rotate annually. See `temporal.md` §KK.7.2.

## See also

- `temporal.md` §13 — Docker infrastructure.
- `temporal.md` §14 — Temporal configuration.
- `temporal.md` §17 — Security (TLS, OIDC).
- `temporal.md` Appendix V — Temporal CLI cookbook.
