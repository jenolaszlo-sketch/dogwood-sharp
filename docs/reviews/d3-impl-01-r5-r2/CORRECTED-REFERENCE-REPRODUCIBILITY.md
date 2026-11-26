# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — Corrected Reference Reproducibility Procedure

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Established for cross-platform qualification

---

## 1. Purpose

This document establishes a **reproducible, platform-independent preparation procedure**
for the corrected Dogwood reference. The procedure:

- Preserves the exact original dependency revision (Dogwood 1.1.2 at `929f49e`)
- Applies the reviewed reference patch deterministically
- Verifies expected original and patched source identities
- Rejects unexpected source or dependency versions
- Builds the reference executable for the actual target platform
- Records toolchain, dependency, executable and patch identities
- Prevents accidental fallback to the uncorrected reference

---

## 2. Committed artifacts

The following artifacts are committed in the R5-R2 evidence tree and are the **sole
authoritative inputs** for reproducing the corrected reference:

| Artifact | Path | SHA-256 |
|----------|------|---------|
| Reference patch | `evidence/differential/corrected-oracle/dogwood-typed-context.patch` | `76714faa4cbbc12866565736f1a6d7b8d4ef02ca53bd90c111eb035a3ee46b32` |
| Preparation script | `evidence/differential/corrected-oracle/prepare-dogwood.ps1` | (content-verified) |
| Cargo manifest template | `evidence/differential/corrected-oracle/Cargo.toml.template` | (content-verified) |
| Oracle source | `evidence/differential/corrected-oracle/src/main.rs` | (content-verified) |
| Lock file (pinned) | `evidence/differential/corrected-oracle/Cargo.lock` | `89c373bdfbdc77abf4d278aed239724083a9cc7d1d4621d18ab26a2dbdf464f9` |

**Expected patched `api.rs` SHA-256**: `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`

**Pinned upstream revision**: `929f49e7305534ac61ef06cf8e47269c596dc851` (Dogwood 1.1.2)

---

## 3. Preparation procedure

### 3.1 Prerequisites

- Rust toolchain 1.99.1 (or compatible stable)
- `cargo` with offline cache populated (or network access to crates.io)
- Git with `core.autocrlf=false`, `core.eol=lf`
- PowerShell 7+ (for the preparation script) or equivalent manual steps
- Work root directory **outside** the dogwood-sharp repository

### 3.2 Automated preparation (PowerShell)

```powershell
# From the R5-R2 evidence directory
cd docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle

# Prepare the corrected oracle build tree
# The work root MUST be outside the dogwood-sharp repository
pwsh -NoProfile -ExecutionPolicy Bypass -File prepare-dogwood.ps1 -WorkRoot "C:\path\to\workroot"
```

The script performs:

1. **Locates the pinned Dogwood checkout** in the local Cargo git cache
   (`~/.cargo/git/checkouts/dogwood-*/929f49e...`) and verifies the revision
   matches `929f49e7305534ac61ef06cf8e47269c596dc851`

2. **Copies the pinned tree byte-for-byte** to `$WorkRoot/dogwood-patched`
   (excluding `target/` and `.git/`)

3. **Applies the patch** with `git apply -p1 --whitespace=nowarn` under
   `core.autocrlf=false`, `core.eol=lf`

4. **Verifies the patched `api.rs` SHA-256** equals
   `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`

5. **Verifies exactly one file differs** from the pinned tree:
   `dogwood-language/src/api.rs`

6. **Stages the oracle build tree** to `$WorkRoot/corrected-oracle` with a
   `Cargo.toml` pointing at the absolute path of the patched dependency

7. **Records provenance** to `$WorkRoot/corrected-oracle-provenance.json`

### 3.3 Manual preparation (cross-platform)

For non-Windows platforms or environments without PowerShell:

```bash
# 1. Locate pinned checkout
PINNED_REV="929f49e7305534ac61ef06cf8e47269c596dc851"
SOURCE=$(find ~/.cargo/git/checkouts -name "dogwood-*" -type d -exec sh -c '
  for d; do
    if git -C "$d" rev-parse HEAD 2>/dev/null | grep -q "^'"$PINNED_REV"'$"; then
      echo "$d"; exit 0
    fi
  done
' sh {} + | head -1)

if [ -z "$SOURCE" ]; then
  echo "Pinned Dogwood revision $PINNED_REV not found in Cargo cache"
  exit 1
fi

# 2. Copy and patch
WORKROOT="/path/to/workroot"
PATCHED="$WORKROOT/dogwood-patched"
STAGING="$WORKROOT/corrected-oracle"

rsync -a --exclude=target --exclude=.git "$SOURCE/" "$PATCHED/"

git -C "$PATCHED" -c core.autocrlf=false -c core.eol=lf \
  apply -p1 --whitespace=nowarn \
  /path/to/dogwood-typed-context.patch

# 3. Verify
ACTUAL=$(sha256sum "$PATCHED/dogwood-language/src/api.rs" | cut -d' ' -f1)
EXPECTED="4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3"
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "Patched api.rs hash mismatch: got $ACTUAL, expected $EXPECTED"
  exit 1
fi

# 4. Stage build tree
mkdir -p "$STAGING/src"
cp -r /path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/src "$STAGING/"
sed "s|__DOGWOOD_PATCHED_PATH__|$PATCHED/dogwood-language|g" \
  /path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/Cargo.toml.template \
  > "$STAGING/Cargo.toml"
cp /path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle/Cargo.lock "$STAGING/"
```

### 3.4 Build the corrected reference oracle

```bash
cd "$STAGING"
cargo build --locked --release --offline
```

The executable will be at `$STAGING/target/release/dogwoodsharp-corrected-oracle`
(or `.exe` on Windows).

### 3.5 Verify the build

```bash
# Verify executable identity
sha256sum "$STAGING/target/release/dogwoodsharp-corrected-oracle*"

# Run a quick smoke test
echo '{"cases":[],"schemaVersion":"3"}' | "$STAGING/target/release/dogwoodsharp-corrected-oracle" /dev/stdin /dev/stdout
```

---

## 4. Recorded identities for R5-R2 qualification

For each differential execution cell, the following identities must be recorded:

| Identity | Source |
|----------|--------|
| Source checkout path | `provenance.sourceCheckout` |
| Pinned revision | `provenance.pinnedRevision` (must equal `929f49e7305534ac61ef06cf8e47269c596dc851`) |
| Patch file | `provenance.patch` (must equal `dogwood-typed-context.patch`) |
| Patch SHA-256 | `provenance.patchSha256` (must equal `76714faa4cbbc12866565736f1a6d7b8d4ef02ca53bd90c111eb035a3ee46b32`) |
| Patched `api.rs` SHA-256 | `provenance.patchedApiSha256` (must equal `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`) |
| Files changed | `provenance.filesChanged` (must equal `["dogwood-language/src/api.rs"]`) |
| Cargo lock hash | SHA-256 of `Cargo.lock` used for build |
| Executable SHA-256 | SHA-256 of built oracle binary |
| Rust toolchain | `rustc --version` output |
| Target platform | `rustc -vV | grep host` output |

---

## 5. Fallback prevention

The preparation procedure **must not** depend on:

- Untracked manual modifications outside the repository
- Environment-specific paths or assumptions
- Implicit Cargo dependency resolution (use `--locked --offline` with committed `Cargo.lock`)
- The uncorrected reference (the original oracle at `docs/reviews/d3-impl-01/evidence/differential/r4/upstream-oracle/`)

The corrected reference is identified by the **patched `api.rs` SHA-256** and the
**provenance record**. Any execution that cannot verify these identities must fail.

---

## 6. Cross-platform execution notes

### Windows (x64)
- Use `prepare-dogwood.ps1` with PowerShell 7+
- Target: `x86_64-pc-windows-msvc`
- Native bridge: `dogwoodsharp_bridge.dll` (ABI 1)

### Linux (x64)
- Use manual preparation with `bash`/`sh`
- Target: `x86_64-unknown-linux-gnu`
- Native bridge: `libdogwoodsharp_bridge.so` (ABI 1)
- May require `libclang` and `pkg-config` for CedarSharp native build

### macOS (ARM64)
- Use manual preparation with `bash`/`sh`
- Target: `aarch64-apple-darwin`
- Native bridge: `libdogwoodsharp_bridge.dylib` (ABI 1)
- Requires Xcode command line tools

---

## 7. Verification checklist for each cell

- [ ] Pinned Dogwood revision `929f49e` located in Cargo cache
- [ ] Patch applies cleanly with `git apply -p1 --whitespace=nowarn`
- [ ] Patched `api.rs` SHA-256 = `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`
- [ ] Exactly one file differs from pinned tree: `dogwood-language/src/api.rs`
- [ ] `Cargo.toml` generated with correct absolute patched dependency path
- [ ] `cargo build --locked --release --offline` succeeds
- [ ] Executable SHA-256 recorded
- [ ] Provenance JSON written and matches expected values
- [ ] Oracle executable runs and produces valid JSON output

---

*This procedure is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission evidence
package. It replaces the Windows-only PowerShell mechanism with a documented,
platform-independent process.*