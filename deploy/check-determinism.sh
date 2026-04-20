#!/bin/bash
# deploy/check-determinism.sh — bash equivalent of scripts/check-determinism.ps1.
# Used in CI (Linux runners). See temporal.md §15.10.
set -euo pipefail

if [ ! -d MagicPAI.Workflows ]; then
    echo "MagicPAI.Workflows/ not yet created (pre-Phase-2). Skipping."
    exit 0
fi

PATTERN='DateTime\.(UtcNow|Now)|Guid\.NewGuid\(\)|new Random|Thread\.Sleep|Task\.Delay'
SAFE='Workflow\.(UtcNow|NewGuid|Random|DelayAsync)'

BAD=$(grep -rnE "$PATTERN" MagicPAI.Workflows/ 2>/dev/null | grep -vE "$SAFE" || true)

if [ -n "$BAD" ]; then
    echo "❌ Non-deterministic APIs in workflow code:"
    echo "$BAD"
    exit 1
fi

echo "✅ Workflow code is deterministic"
