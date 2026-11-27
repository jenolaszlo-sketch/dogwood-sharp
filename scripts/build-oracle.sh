#!/bin/bash
set -euo pipefail

# GNU coreutils are not guaranteed on macOS. Provide sha256sum/realpath shims.
if ! command -v sha256sum >/dev/null 2>&1; then
  sha256sum() { shasum -a 256 "$@"; }
fi
if ! command -v realpath >/dev/null 2>&1; then
  realpath() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }
fi
# On Windows, Git's /usr/bin/link shadows MSVC link.exe. Prefer the MSVC
# linker directory when the workflow supplies it.
if [ -n "${MSVC_LINK_DIR:-}" ] && command -v cygpath >/dev/null 2>&1; then
  export PATH="$(cygpath -u "$MSVC_LINK_DIR"):$PATH"
fi

patchedRoot="${1}"
staging="r5r3-work/corrected-oracle"

if [ -d "$staging" ]; then rm -rf "$staging"; fi
mkdir -p "$staging/src"
cp -r docs/reviews/d3-impl-01-r5-r3/evidence/differential/corrected-oracle/src "$staging/"

template=$(cat docs/reviews/d3-impl-01-r5-r3/evidence/differential/corrected-oracle/Cargo.toml.template)
patchedDogwood="$(realpath "$patchedRoot/dogwood-language")"
# Cargo on Windows must receive a Windows-style path; MSYS realpath returns
# /d/..., which Cargo reads as D:\d\...
if command -v cygpath >/dev/null 2>&1; then
  patchedDogwood="$(cygpath -m "$patchedDogwood")"
fi
manifest="${template/__DOGWOOD_PATCHED_PATH__/$patchedDogwood}"
echo "$manifest" > "$staging/Cargo.toml"

cp docs/reviews/d3-impl-01-r5-r3/evidence/differential/corrected-oracle/Cargo.lock "$staging/"

export CARGO_TARGET_DIR="r5r3-work/oracle-target"
cargo build --locked --release --manifest-path "$staging/Cargo.toml"

suffix=""
if [ "$OSTYPE" = "msys" ] || [ "$OSTYPE" = "cygwin" ]; then suffix=".exe"; fi
oracleExe="r5r3-work/oracle-target/release/dogwoodsharp-corrected-oracle$suffix"
if [ ! -f "$oracleExe" ]; then echo "Oracle executable not found at $oracleExe"; exit 1; fi

oracleSha=$(sha256sum "$oracleExe" | cut -d' ' -f1)
echo "Oracle built: $oracleExe (SHA256: $oracleSha)"
echo "path=$oracleExe" >> $GITHUB_OUTPUT
echo "sha256=$oracleSha" >> $GITHUB_OUTPUT