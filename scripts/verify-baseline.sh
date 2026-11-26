#!/bin/bash
set -euo pipefail

# Verify baseline commit and tree
actualCommit=$(git rev-parse HEAD)
if [ "$actualCommit" != "${GITHUB_SHA}" ]; then
  echo "Checkout commit mismatch: expected ${GITHUB_SHA}, got $actualCommit"
  exit 1
fi
actualTree=$(git rev-parse HEAD^{tree})
echo "Baseline verified: $actualCommit / $actualTree"
echo "R5R3_ACTUAL_TREE=$actualTree" >> $GITHUB_ENV