# Phase 1 — Day 1: Infrastructure setup

**Objective:** bring up the Temporal stack alongside the existing Elsa dev
environment. Both run simultaneously; traffic still routes to Elsa.

**Duration:** ~4 hours.
**Prerequisites:**
- Docker Desktop installed and running.
- .NET 10 SDK installed (`dotnet --version` ≥ 10.0.100).
- Repo cloned, branch `temporal` checked out.
- (Windows) PowerShell 7+.

---

## Steps

### Step 1: Verify you're on the right branch

```powershell
git status
git branch --show-current   # should print "temporal"
```

### Step 2: Build worker-env image (if not cached)

```powershell
./scripts/dev-up.ps1 -SkipWorkerBuild:$false
# (or if first time)
docker compose -f docker/docker-compose.yml --profile build build worker-env-builder
```

Verify:
```powershell
docker image inspect magicpai-env:latest
# Expect output, not error
```

### Step 3: Start existing Elsa stack

```powershell
docker compose -f docker/docker-compose.yml up -d
# Wait for health
Start-Sleep 5
Invoke-WebRequest http://localhost:5000/health
```

### Step 4: Add Temporal stack alongside

```powershell
docker compose -f docker/docker-compose.temporal.yml up -d
# Wait for Temporal health
for ($i=0; $i -lt 30; $i++) {
    try {
        docker exec mpai-temporal temporal operator cluster health 2>$null
        Write-Host "✅ Temporal healthy"
        break
    } catch { Start-Sleep 2 }
}
```

Verify:
```powershell
# Temporal UI reachable
Start-Process http://localhost:8233
# Expect "MagicPAI" namespace visible (it's the default from compose env var)

# Temporal CLI works
./scripts/temporal-cli.ps1 operator namespace describe
# Should print namespace details
```

### Step 5: Register search attributes

```powershell
./scripts/temporal-cli.ps1 operator search-attribute create `
    --name MagicPaiAiAssistant --type Text `
    --name MagicPaiModel --type Text `
    --name MagicPaiWorkflowType --type Text `
    --name MagicPaiSessionKind --type Text `
    --name MagicPaiCostUsdBucket --type Int
```

Expect: `Search attributes have been added`.

Verify:
```powershell
./scripts/temporal-cli.ps1 operator search-attribute list
# Should list the above attributes
```

### Step 6: Add Temporalio NuGet packages to MagicPAI.Server.csproj

Edit `MagicPAI.Server/MagicPAI.Server.csproj`. In the main `<ItemGroup>` that has
other `<PackageReference>` entries, add:

```xml
<PackageReference Include="Temporalio" Version="1.13.0" />
<PackageReference Include="Temporalio.Extensions.Hosting" Version="1.13.0" />
```

(Leave the Elsa packages; Phase 3 removes them.)

### Step 7: Restore

```powershell
dotnet restore MagicPAI.Server/MagicPAI.Server.csproj
```

Expect: Temporalio 1.13.0 + transitive deps installed.

### Step 8: Verify build still works

```powershell
dotnet build MagicPAI.Server/MagicPAI.Server.csproj
```

Expect: zero warnings, zero errors. (Elsa + Temporal coexist cleanly.)

### Step 9: Commit and push

```powershell
git add MagicPAI.Server/MagicPAI.Server.csproj MagicPAI.Server/packages.lock.json
git commit -m "temporal: Phase 1 day 1 — add Temporalio packages alongside Elsa"
# Do not push yet unless approved
```

### Step 10: Update SCORECARD.md

Open `SCORECARD.md`. In the Phase 1 section, check off:
- [x] `docker/docker-compose.temporal.yml` created
- [x] `docker/temporal/dynamicconfig/development.yaml` created
- [x] Temporal stack runs healthy locally
- [x] Temporal UI accessible at http://localhost:8233
- [x] `Temporalio` NuGet packages added to `MagicPAI.Server.csproj`

Commit the scorecard update.

---

## Definition of done for Day 1

- [ ] `docker compose ps` shows both the original Elsa services AND the new
      Temporal services (`mpai-temporal`, `mpai-temporal-db`, `mpai-temporal-ui`)
      all healthy.
- [ ] `http://localhost:5000/health` still returns 200 (Elsa side unaffected).
- [ ] `http://localhost:8233/namespaces/magicpai` loads in browser.
- [ ] `dotnet build` succeeds with Temporalio packages added.
- [ ] Search attributes registered.
- [ ] SCORECARD.md updated.
- [ ] Commits pushed to `temporal` branch.

If any of these fail, troubleshoot before proceeding to Day 2.

## Troubleshooting

**Temporal not reaching health:**
```powershell
docker compose logs temporal
# Look for DB connection errors. If Temporal can't reach temporal-db,
# verify both are on the same Docker network.
```

**Port 7233 already in use:**
```powershell
# Find what's using it
netstat -ano | findstr :7233
# Kill or use a different port via docker-compose override
```

**Temporal CLI not found in container:**
```powershell
docker exec mpai-temporal which temporal
# The auto-setup image includes `temporal` CLI at /usr/local/bin/
```

**Packages.lock.json conflicts:**
If you get package restore errors, delete all `packages.lock.json` files and restore fresh:
```powershell
Get-ChildItem -Filter packages.lock.json -Recurse | Remove-Item
dotnet restore
```

## Next: Phase 1 Day 2

See `docs/phase-guides/Phase1-Day2.md` (to be created) — first activity group port
(`DockerActivities`).

## Time spent

Record actual time spent:
- Setup + reading: ___ min
- Step execution: ___ min
- Troubleshooting: ___ min
- Total: ___ min

Report to `docs/agent-session-log.md` if AI-assisted.
