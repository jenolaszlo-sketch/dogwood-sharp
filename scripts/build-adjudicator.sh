#!/bin/bash
set -euo pipefail

staging="r5r3-work/cedar-adjudicator"
if [ -d "$staging" ]; then rm -rf "$staging"; fi
mkdir -p "$staging"
cp -r docs/reviews/d3-impl-01-r5-r3/evidence/differential/cedar-adjudicator/src "$staging/"
cp docs/reviews/d3-impl-01-r5-r3/evidence/differential/cedar-adjudicator/Cargo.toml "$staging/"
cp docs/reviews/d3-impl-01-r5-r3/evidence/differential/cedar-adjudicator/Cargo.lock "$staging/"

export CARGO_TARGET_DIR="r5r3-work/adjudicator-target"
cargo build --locked --release --offline --manifest-path "$staging/Cargo.toml"

suffix=""
if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then suffix=".exe"; fi
exe="r5r3-work/adjudicator-target/release/cedar-adjudicator$suffix"
if [ ! -f "$exe" ]; then echo "Adjudicator not found at $exe"; exit 1; fi

sha=$(sha256sum "$exe" | cut -d' ' -f1)
echo "Cedar adjudicator built: $exe (SHA256: $sha)"
echo "path=$exe" >> $GITHUB_OUTPUT
echo "sha256=$sha" >> $GITHUB_OUTPUT