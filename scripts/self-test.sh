#!/bin/bash
set -euo pipefail

probe="docs/reviews/d3-impl-01-r5-r3/evidence/differential/probe/bin/Release/${TFM}/PrepProbe.dll"
oracle="docs/reviews/d3-impl-01/evidence/differential/r4/upstream-oracle/target/release/dogwoodsharp-upstream-oracle.exe"
if [ ! -f "$oracle" ]; then
  echo "Original oracle not available on non-Windows, skipping self-test"
  exit 0
fi
outDir="artifacts/r5r3-differential/${RID}/${TFM}"
selfTest="$outDir/self-test.json"
dotnet "$probe" self-test "$oracle" "$selfTest" 2>&1 | tee "$outDir/self-test.log"
if [ $? -ne 0 ]; then echo "Self-test failed"; exit 1; fi
echo "Self-test passed"