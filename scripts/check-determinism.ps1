# scripts/check-determinism.ps1
# CI-friendly determinism grep. Fails if non-deterministic APIs are used in workflow code.
# See temporal.md §UU.7 and §25.

$ErrorActionPreference = 'Stop'

$pattern = 'DateTime\.(UtcNow|Now)|Guid\.NewGuid\(\)|new Random|Thread\.Sleep|Task\.Delay'
$safePattern = 'Workflow\.(UtcNow|NewGuid|Random|DelayAsync)'

if (-not (Test-Path "MagicPAI.Workflows")) {
    Write-Host "MagicPAI.Workflows/ not yet created (pre-Phase-2). Skipping." -ForegroundColor DarkGray
    exit 0
}

$hits = Get-ChildItem -Path "MagicPAI.Workflows" -Recurse -Filter "*.cs" |
    Select-String -Pattern $pattern |
    Where-Object { $_.Line -notmatch $safePattern }

if ($hits) {
    Write-Host "[-] Non-deterministic APIs in workflow code:" -ForegroundColor Red
    foreach ($hit in $hits) {
        Write-Host ("  {0}:{1}: {2}" -f $hit.Filename, $hit.LineNumber, $hit.Line.Trim())
    }
    exit 1
}

Write-Host "[+] Workflow code is deterministic (no forbidden APIs)" -ForegroundColor Green
exit 0
