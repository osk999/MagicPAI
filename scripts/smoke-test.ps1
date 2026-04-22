# scripts/smoke-test.ps1
# Runs a full-path smoke test: create a session, poll until complete.
# See temporal.md §UU.3 / §19.17.
param(
    [string]$Base = "http://localhost:5000",
    [string]$WorkflowType = "SimpleAgent",
    [string]$AiAssistant = "claude",
    [string]$Model = "haiku",
    [int]$ModelPower = 3,
    [int]$PollIntervalSec = 5,
    [int]$MaxPolls = 60
)

$ErrorActionPreference = 'Stop'

Write-Host "=== MagicPAI smoke test against $Base ===" -ForegroundColor Cyan
Write-Host "Workflow: $WorkflowType / $AiAssistant / $Model / Power=$ModelPower"
Write-Host ""

$body = @{
    prompt = "Print hello world"
    workflowType = $WorkflowType
    aiAssistant = $AiAssistant
    model = $Model
    modelPower = $ModelPower
    workspacePath = "/tmp/smoke-$(Get-Date -Format 'yyyyMMddHHmmss')"
    enableGui = $false
} | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$Base/api/sessions" `
        -Method Post -ContentType 'application/json' -Body $body -ErrorAction Stop
    $sid = $resp.sessionId
    Write-Host "Started session: $sid" -ForegroundColor Green
    Write-Host "Temporal UI: http://localhost:8233/namespaces/magicpai/workflows/$sid"
} catch {
    Write-Host "[-] Failed to create session: $_" -ForegroundColor Red
    exit 1
}

$start = Get-Date
for ($i = 1; $i -le $MaxPolls; $i++) {
    Start-Sleep -Seconds $PollIntervalSec
    try {
        $status = (Invoke-RestMethod -Uri "$Base/api/sessions/$sid" -ErrorAction Stop).status
    } catch {
        Write-Host "[$i] Error polling: $_" -ForegroundColor Yellow
        continue
    }
    $elapsed = [math]::Round((Get-Date).Subtract($start).TotalSeconds, 1)
    Write-Host "[$i] ${elapsed}s Status: $status"

    switch ($status) {
        "Completed"  { Write-Host "[+] SUCCESS" -ForegroundColor Green; exit 0 }
        "Failed"     { Write-Host "[-] FAILED" -ForegroundColor Red; exit 1 }
        "Cancelled"  { Write-Host "[-] CANCELLED" -ForegroundColor Red; exit 1 }
        "Terminated" { Write-Host "[-] TERMINATED" -ForegroundColor Red; exit 1 }
    }
}

Write-Host "[-] TIMEOUT after $($MaxPolls * $PollIntervalSec)s" -ForegroundColor Red
exit 1
