#!/bin/bash
set -euo pipefail

outDir="artifacts/r5r3-differential/${RID}/${TFM}"
obs="$outDir/observations-corrected-reference.json"
result=$(cat "$obs")

echo "=== ${RID} / ${TFM} ==="
echo "Total: $(echo "$result" | jq '.summary.total')"
echo "Passed: $(echo "$result" | jq '.summary.passed')"
echo "Failed: $(echo "$result" | jq '.summary.failed')"
echo "Pending: $(echo "$result" | jq '.summary.pending')"

failed=$(echo "$result" | jq '.summary.failed')
if [ "$failed" -gt 0 ]; then
  echo "FAILURES:"
  echo "$result" | jq -r '.cases[] | select(.verdict != "pass") | "  \(.id): \(.classification) fields=\(.observedDifferenceFields[].field)"'
  exit 1
fi

coverageErrors=$(echo "$result" | jq -r '.coverageErrors[]?' 2>/dev/null)
if [ -n "$coverageErrors" ]; then
  echo "Coverage errors: $coverageErrors"
  exit 1
fi

echo "ALL CASES PASSED on ${RID} / ${TFM}"