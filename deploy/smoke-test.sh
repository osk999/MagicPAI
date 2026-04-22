#!/bin/bash
# deploy/smoke-test.sh — cross-platform smoke test. See temporal.md §19.17.
set -euo pipefail

BASE=${1:-http://localhost:5000}
WORKFLOW=${2:-SimpleAgent}
ASSISTANT=${3:-claude}
MODEL=${4:-haiku}

echo "=== MagicPAI smoke test against $BASE ==="
echo "Workflow: $WORKFLOW / $ASSISTANT / $MODEL"

RESP=$(curl -fsS -X POST "$BASE/api/sessions" \
    -H 'Content-Type: application/json' \
    -d "{
        \"prompt\": \"Print hello world\",
        \"workflowType\": \"$WORKFLOW\",
        \"aiAssistant\": \"$ASSISTANT\",
        \"model\": \"$MODEL\",
        \"modelPower\": 3,
        \"workspacePath\": \"/tmp/smoke-$(date +%s)\",
        \"enableGui\": false
    }")
SID=$(echo "$RESP" | jq -r .sessionId)
echo "Started session: $SID"
echo "Temporal UI: http://localhost:8233/namespaces/magicpai/workflows/$SID"

START=$(date +%s)
for i in $(seq 1 60); do
    sleep 5
    STATUS=$(curl -fsS "$BASE/api/sessions/$SID" | jq -r .status)
    ELAPSED=$(( $(date +%s) - START ))
    echo "[$i] ${ELAPSED}s Status: $STATUS"
    case "$STATUS" in
        Completed) echo "✅ SUCCESS"; exit 0 ;;
        Failed|Cancelled|Terminated) echo "❌ $STATUS"; exit 1 ;;
    esac
done

echo "❌ TIMEOUT"
exit 1
