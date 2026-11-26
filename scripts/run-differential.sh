#!/bin/bash
set -euo pipefail

probe="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/${TFM}/PrepProbe.dll"
oracle="${ORACLE_PATH}"
fixtures="docs/reviews/d3-impl-01-r5-r3/evidence/fixtures.json"
register="docs/reviews/d3-impl-01-r5-r3/evidence/register-corrected-reference.json"
outDir="artifacts/r5r3-differential/${RID}/${TFM}"
mkdir -p "$outDir"
obs="$outDir/observations-corrected-reference.json"
life="$outDir/lifecycle-corrected-reference.json"
scratch="r5r3-work/scratch-${RID}-${TFM}"

dotnet "$probe" differential "$fixtures" "$oracle" "$register" "$obs" "$life" "$scratch" 2>&1 | tee "$outDir/console.log"
exitCode=$?

if [ $exitCode -ne 0 ]; then
  echo "Differential exited with code $exitCode"
  result=$(cat "$obs")
  echo "Summary: $(echo "$result" | jq -c '.summary')"
  echo "$result" | jq -r '.coverageErrors[]?' 2>/dev/null | while read err; do echo "Coverage error: $err"; done
  echo "$result" | jq -r '.cases[] | select(.verdict != "pass") | "FAIL: \(.id) class=\(.classification) fields=\(.observedDifferenceFields[].field)"' 2>/dev/null
  exit 1
fi

result=$(cat "$obs")
echo "Differential passed: $(echo "$result" | jq '.summary.passed')/$(echo "$result" | jq '.summary.total') cases"
echo "observations=$obs" >> $GITHUB_OUTPUT
echo "lifecycle=$life" >> $GITHUB_OUTPUT