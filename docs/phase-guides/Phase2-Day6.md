# Phase 2 — Day 6: Workflow contracts

**Objective:** create all 15 workflow input/output contract files.

**Duration:** ~4 hours.
**Prerequisites:** Day 5 complete.

---

## Steps

### Step 1: Create Common contract

`MagicPAI.Workflows/Contracts/Common.cs` per §6.2:
- `ModelSpec`
- `SessionContext`
- `VerifyGateSpec`
- `VerifyResult`
- `CostEntry`

### Step 2: Per-workflow contract files

Create one file per workflow in `MagicPAI.Workflows/Contracts/`:

1. `SimpleAgentContracts.cs` — already exists from Day 3.
2. `VerifyAndRepairContracts.cs` (§H.1)
3. `PromptEnhancerContracts.cs` (§H.2)
4. `ContextGathererContracts.cs` (§H.3)
5. `PromptGroundingContracts.cs` (§H.4)
6. `OrchestrateSimpleContracts.cs` (§H.5)
7. `OrchestrateComplexContracts.cs` (§H.5/§8.5)
8. `ComplexTaskWorkerContracts.cs` (§H.6)
9. `PostExecutionContracts.cs` (§H.7)
10. `ResearchPipelineContracts.cs` (§H.8)
11. `StandardOrchestrateContracts.cs` (§H.9)
12. `ClawEvalAgentContracts.cs` (§H.10)
13. `WebsiteAuditContracts.cs` (§H.11 + §H.12)
14. `FullOrchestrateContracts.cs` (§8.6)
15. `DeepResearchContracts.cs` (§H.13)

Each file contains 1-2 `record` types (Input + Output).

### Step 3: Build

```powershell
dotnet build MagicPAI.Workflows
```

Expected: no errors. Contracts are just records with no logic.

### Step 4: Commit

```powershell
git add MagicPAI.Workflows/Contracts/
git commit -m "temporal: workflow contracts (15 workflows' input/output records)"
```

---

## Definition of done

- [ ] 15 contract files in `MagicPAI.Workflows/Contracts/`.
- [ ] `Common.cs` shared records created.
- [ ] `dotnet build` clean for MagicPAI.Workflows.
- [ ] Commit pushed.

## Next

`Phase2-Day7.md` — simple workflows (VerifyAndRepair, PromptEnhancer, ContextGatherer,
PromptGrounding).
