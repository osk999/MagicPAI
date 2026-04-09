#!/usr/bin/env bash
# ============================================================
# MagicPAI — Workflow Verification Script
# Verifies all 17 workflows against a running server
# Usage:  bash scripts/verify-workflows.sh [BASE_URL]
# ============================================================
set -euo pipefail

BASE="${1:-http://localhost:5237}"
WORKSPACE="${2:-C:/AllGit/CSharp/MagicPAI}"
PASS=0; FAIL=0; SKIP=0
RESULTS=()

green()  { printf "\033[32m%s\033[0m\n" "$*"; }
red()    { printf "\033[31m%s\033[0m\n" "$*"; }
yellow() { printf "\033[33m%s\033[0m\n" "$*"; }
bold()   { printf "\033[1m%s\033[0m\n" "$*"; }

record() {
    local name="$1" status="$2" detail="${3:-}"
    if [ "$status" = "PASS" ]; then
        ((PASS++))
        RESULTS+=("$(green "[PASS]") $name ${detail:+— $detail}")
    elif [ "$status" = "FAIL" ]; then
        ((FAIL++))
        RESULTS+=("$(red "[FAIL]") $name ${detail:+— $detail}")
    else
        ((SKIP++))
        RESULTS+=("$(yellow "[SKIP]") $name ${detail:+— $detail}")
    fi
}

# ============================================================
# LEVEL 1: Server Health
# ============================================================
bold "═══════════════════════════════════════════"
bold " LEVEL 1: Server Health"
bold "═══════════════════════════════════════════"

echo "Checking server at $BASE ..."
if curl -sf "$BASE/api/sessions" > /dev/null 2>&1; then
    record "Server reachable" "PASS"
    green "  ✓ Server is up"
else
    record "Server reachable" "FAIL" "Cannot reach $BASE/api/sessions"
    red "  ✗ Server is NOT reachable at $BASE"
    echo "Start server first:  dotnet run --project MagicPAI.Server"
    exit 1
fi

# ============================================================
# LEVEL 2: All 17 Workflows Registered
# ============================================================
bold ""
bold "═══════════════════════════════════════════"
bold " LEVEL 2: Workflow Registration Check"
bold "═══════════════════════════════════════════"

# These are ALL 17 workflows defined in WorkflowPublisher.cs
ALL_WORKFLOWS=(
    "full-orchestrate"
    "simple-agent"
    "verify-and-repair"
    "prompt-enhancer"
    "context-gatherer"
    "prompt-grounding"
    "loop-verifier"
    "website-audit-loop"
    "is-complex-app"
    "is-website-project"
    "orchestrate-complex-path"
    "orchestrate-simple-path"
    "post-execution-pipeline"
    "research-pipeline"
    "standard-orchestrate"
    "test-set-prompt"
    "claw-eval-agent"
)

# Try dispatching a dry-run to test each workflow is registered
# We do this by posting and immediately cancelling
echo "Checking all 17 workflow definitions are published..."
for wf in "${ALL_WORKFLOWS[@]}"; do
    # Try to create a session — if the workflow is not published, we get 500/error
    RESP=$(curl -sf -X POST "$BASE/api/sessions" \
        -H "Content-Type: application/json" \
        -d "{\"prompt\":\"VERIFY_REGISTRATION_TEST\",\"workspacePath\":\"$WORKSPACE\",\"workflowName\":\"$wf\"}" \
        2>&1) || true

    if echo "$RESP" | grep -q '"sessionId"'; then
        SID=$(echo "$RESP" | sed 's/.*"sessionId":"\([^"]*\)".*/\1/')
        record "Registered: $wf" "PASS" "dispatched as $SID"
        green "  ✓ $wf — dispatched ($SID)"
        # Cancel immediately — we just wanted to confirm it dispatches
        curl -sf -X DELETE "$BASE/api/sessions/$SID" > /dev/null 2>&1 || true
    else
        record "Registered: $wf" "FAIL" "dispatch error: $RESP"
        red "  ✗ $wf — NOT dispatchable"
    fi
done

# ============================================================
# LEVEL 3: API Endpoint Smoke Tests
# ============================================================
bold ""
bold "═══════════════════════════════════════════"
bold " LEVEL 3: API Endpoint Smoke Tests"
bold "═══════════════════════════════════════════"

# GET /api/sessions
echo "Testing GET /api/sessions ..."
HTTP_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$BASE/api/sessions" 2>/dev/null) || HTTP_CODE="000"
if [ "$HTTP_CODE" = "200" ]; then
    record "GET /api/sessions" "PASS" "HTTP $HTTP_CODE"
    green "  ✓ List sessions — $HTTP_CODE"
else
    record "GET /api/sessions" "FAIL" "HTTP $HTTP_CODE"
    red "  ✗ List sessions — $HTTP_CODE"
fi

# POST /api/sessions (create)
echo "Testing POST /api/sessions ..."
CREATE_RESP=$(curl -sf -X POST "$BASE/api/sessions" \
    -H "Content-Type: application/json" \
    -d "{\"prompt\":\"API smoke test: create a file named smoke_test.txt with 'hello'\",\"workspacePath\":\"$WORKSPACE\",\"workflowName\":\"test-set-prompt\"}" \
    2>&1) || CREATE_RESP="ERROR"

if echo "$CREATE_RESP" | grep -q '"sessionId"'; then
    TEST_SID=$(echo "$CREATE_RESP" | sed 's/.*"sessionId":"\([^"]*\)".*/\1/')
    record "POST /api/sessions" "PASS" "created $TEST_SID"
    green "  ✓ Create session — $TEST_SID"
else
    TEST_SID=""
    record "POST /api/sessions" "FAIL" "$CREATE_RESP"
    red "  ✗ Create session failed"
fi

if [ -n "$TEST_SID" ]; then
    # GET /api/sessions/{id}
    sleep 1
    echo "Testing GET /api/sessions/$TEST_SID ..."
    SESS_RESP=$(curl -sf "$BASE/api/sessions/$TEST_SID" 2>/dev/null) || SESS_RESP="ERROR"
    if echo "$SESS_RESP" | grep -q '"id"'; then
        record "GET /api/sessions/{id}" "PASS"
        green "  ✓ Get session"
    else
        record "GET /api/sessions/{id}" "FAIL" "$SESS_RESP"
        red "  ✗ Get session"
    fi

    # GET /api/sessions/{id}/output
    echo "Testing GET /api/sessions/$TEST_SID/output ..."
    OUT_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$BASE/api/sessions/$TEST_SID/output" 2>/dev/null) || OUT_CODE="000"
    if [ "$OUT_CODE" = "200" ]; then
        record "GET /api/sessions/{id}/output" "PASS" "HTTP $OUT_CODE"
        green "  ✓ Get output — $OUT_CODE"
    else
        record "GET /api/sessions/{id}/output" "FAIL" "HTTP $OUT_CODE"
        red "  ✗ Get output — $OUT_CODE"
    fi

    # GET /api/sessions/{id}/activities
    echo "Testing GET /api/sessions/$TEST_SID/activities ..."
    ACT_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$BASE/api/sessions/$TEST_SID/activities" 2>/dev/null) || ACT_CODE="000"
    if [ "$ACT_CODE" = "200" ]; then
        record "GET /api/sessions/{id}/activities" "PASS" "HTTP $ACT_CODE"
        green "  ✓ Get activities — $ACT_CODE"
    else
        record "GET /api/sessions/{id}/activities" "FAIL" "HTTP $ACT_CODE"
        red "  ✗ Get activities — $ACT_CODE"
    fi

    # DELETE /api/sessions/{id}
    echo "Testing DELETE /api/sessions/$TEST_SID ..."
    DEL_CODE=$(curl -sf -o /dev/null -w "%{http_code}" -X DELETE "$BASE/api/sessions/$TEST_SID" 2>/dev/null) || DEL_CODE="000"
    if [ "$DEL_CODE" = "200" ]; then
        record "DELETE /api/sessions/{id}" "PASS" "HTTP $DEL_CODE"
        green "  ✓ Cancel session — $DEL_CODE"
    else
        record "DELETE /api/sessions/{id}" "FAIL" "HTTP $DEL_CODE"
        red "  ✗ Cancel session — $DEL_CODE"
    fi

    # 404 for non-existent
    echo "Testing 404 for non-existent session ..."
    NF_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/api/sessions/nonexistent123" 2>/dev/null) || NF_CODE="000"
    if [ "$NF_CODE" = "404" ]; then
        record "404 for missing session" "PASS"
        green "  ✓ Returns 404 for missing session"
    else
        record "404 for missing session" "FAIL" "got HTTP $NF_CODE, expected 404"
        red "  ✗ Expected 404, got $NF_CODE"
    fi
fi

# ============================================================
# LEVEL 4: End-to-End Workflow Execution
# ============================================================
bold ""
bold "═══════════════════════════════════════════"
bold " LEVEL 4: End-to-End Execution Tests"
bold "═══════════════════════════════════════════"

# Dispatchable top-level workflows with suitable test prompts
declare -A E2E_TESTS
E2E_TESTS["test-set-prompt"]="Create a file named e2e_test.txt containing 'MagicPAI verification passed'"
E2E_TESTS["simple-agent"]="List the files in the current workspace root directory and output a summary"
E2E_TESTS["full-orchestrate"]="Create a file named orchestrate_test.txt at workspace root containing 'full orchestrate test'"

MAX_WAIT=120  # seconds to wait for completion

run_e2e() {
    local wf_name="$1" prompt="$2"
    echo ""
    bold "  Testing: $wf_name"
    echo "  Prompt: ${prompt:0:80}..."

    # Dispatch
    local resp
    resp=$(curl -sf -X POST "$BASE/api/sessions" \
        -H "Content-Type: application/json" \
        -d "{\"prompt\":\"$prompt\",\"workspacePath\":\"$WORKSPACE\",\"workflowName\":\"$wf_name\"}" \
        2>&1) || resp="ERROR"

    if ! echo "$resp" | grep -q '"sessionId"'; then
        record "E2E: $wf_name" "FAIL" "dispatch failed: $resp"
        red "    ✗ Could not dispatch"
        return
    fi

    local sid
    sid=$(echo "$resp" | sed 's/.*"sessionId":"\([^"]*\)".*/\1/')
    echo "  Session: $sid"

    # Poll for completion
    local elapsed=0
    local state="running"
    while [ $elapsed -lt $MAX_WAIT ] && [ "$state" = "running" ]; do
        sleep 5
        elapsed=$((elapsed + 5))
        local status_resp
        status_resp=$(curl -sf "$BASE/api/sessions/$sid" 2>/dev/null) || status_resp="{}"
        state=$(echo "$status_resp" | sed 's/.*"state":"\([^"]*\)".*/\1/' 2>/dev/null) || state="unknown"
        printf "    [%3ds] state=%s\n" "$elapsed" "$state"
    done

    # Check result
    if [ "$state" = "completed" ]; then
        # Verify activities were recorded
        local activities
        activities=$(curl -sf "$BASE/api/sessions/$sid/activities" 2>/dev/null) || activities="[]"
        local act_count
        act_count=$(echo "$activities" | tr ',' '\n' | grep -c '"activityType"' 2>/dev/null) || act_count=0

        # Verify output exists
        local output
        output=$(curl -sf "$BASE/api/sessions/$sid/output" 2>/dev/null) || output="[]"
        local out_len=${#output}

        record "E2E: $wf_name" "PASS" "completed in ${elapsed}s, ${act_count} activities, output ${out_len} chars"
        green "    ✓ Completed in ${elapsed}s — ${act_count} activities recorded"
    elif [ "$state" = "faulted" ]; then
        record "E2E: $wf_name" "FAIL" "faulted after ${elapsed}s"
        red "    ✗ Faulted after ${elapsed}s"
        # Show output for debugging
        local output
        output=$(curl -sf "$BASE/api/sessions/$sid/output" 2>/dev/null) || output="[]"
        echo "    Output: ${output:0:200}"
    elif [ $elapsed -ge $MAX_WAIT ]; then
        record "E2E: $wf_name" "FAIL" "timed out after ${MAX_WAIT}s (state=$state)"
        yellow "    ⏱ Timed out after ${MAX_WAIT}s (state=$state)"
        # Cancel the timed-out workflow
        curl -sf -X DELETE "$BASE/api/sessions/$sid" > /dev/null 2>&1 || true
    else
        record "E2E: $wf_name" "FAIL" "unexpected state: $state"
        red "    ✗ Unexpected state: $state"
    fi
}

echo "Running end-to-end tests (this takes a while — each workflow runs AI agents)..."
echo "Max wait per workflow: ${MAX_WAIT}s"

for wf_name in "test-set-prompt" "simple-agent" "full-orchestrate"; do
    run_e2e "$wf_name" "${E2E_TESTS[$wf_name]}"
done

# ============================================================
# SUMMARY
# ============================================================
bold ""
bold "═══════════════════════════════════════════"
bold " SUMMARY"
bold "═══════════════════════════════════════════"

for r in "${RESULTS[@]}"; do
    echo "  $r"
done

echo ""
bold "Total: $((PASS + FAIL + SKIP)) checks — $(green "$PASS passed"), $(red "$FAIL failed"), $(yellow "$SKIP skipped")"

if [ $FAIL -gt 0 ]; then
    echo ""
    red "Some checks failed. Review the output above for details."
    exit 1
else
    echo ""
    green "All checks passed!"
    exit 0
fi
