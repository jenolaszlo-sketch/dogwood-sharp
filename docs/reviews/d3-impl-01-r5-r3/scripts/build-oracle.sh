#!/bin/bash
set -euo pipefail

patchedRoot="${1}"
staging="r5r3-work/corrected-oracle"

if [ -d "$staging" ]; then rm -rf "$staging"; fi
mkdir -p "$staging/src"
cp -r docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/src "$staging/"

template=$(cat docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/Cargo.toml.template)
manifest="${template/__DOGWOOD_PATCHED_PATH__/$(realpath "$patchedRoot/dogwood-language")}"
echo "$manifest" > "$staging/Cargo.toml"

cp docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/Cargo.lock "$staging/"

export CARGO_TARGET_DIR="r5r3-work/oracle-target"
cargo build --locked --release --offline --manifest-path "$staging/Cargo.toml"

suffix=""
if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then suffix=".exe"; fi
oracleExe="r5r3-work/oracle-target/release/dogwoodsharp-corrected-oracle$suffix"
if [ ! -f "$oracleExe" ]; then echo "Oracle executable not found at $oracleExe"; exit 1; fi

oracleSha=$(sha256sum "$oracleExe" | cut -d' ' -f1)
echo "Oracle built: $oracleExe (SHA256: $oracleSha)"
echo "path=$oracleExe" >> $GITHUB_OUTPUT
echo "sha256=$oracleSha" >> $GITHUB_OUTPUT