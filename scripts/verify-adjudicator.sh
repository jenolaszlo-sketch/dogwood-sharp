#!/bin/bash
set -euo pipefail

adj="${ADJUDICATOR_PATH}"
# The R5-R3 snapshot retained the adjudicator source but not its corpus; the
# 57-case corpus and its authority record live in the preserved R5-R2 evidence.
corpus="docs/reviews/d3-impl-01-r5-r2/evidence/differential/cedar-adjudicator/corpus.json"
outDir="artifacts/r5r3-differential/${RID}/${TFM}"
outcome="$outDir/adjudicator-outcome.json"
"$adj" "$corpus" "$outcome"
if [ $? -ne 0 ]; then echo "Cedar adjudicator failed"; exit 1; fi
echo "Cedar adjudicator verification passed"