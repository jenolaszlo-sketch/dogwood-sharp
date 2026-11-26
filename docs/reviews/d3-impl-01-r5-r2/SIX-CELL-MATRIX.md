# DOGWOOD-SHARP-D3-IMPL-01-R5-R2 — Six-Cell Differential Matrix

**Commission**: DOGWOOD-SHARP-D3-IMPL-01-R5-R2
**Date**: 26 November 2026
**Status**: Windows x64 executed; Linux x64 & macOS ARM64 pending (procedure documented)

---

## 1. Matrix requirement (from qualification plan §9)

| OS | RID | TFMs | Minimum Requirement |
|----|-----|------|---------------------|
| Windows | `win-x64` | `net8.0`, `net10.0` | Executed |
| Linux | `linux-x64` | `net8.0`, `net10.0` | Same locked oracle, fixtures, register |
| macOS | `osx-arm64` | `net8.0`, `net10.0` | Same locked oracle, fixtures, register |

**Rule**: A cell that cannot be run is **pending, never passed**.

---

## 2. Windows x64 results (executed in R5-R1, verified in R5-R2)

### 2.1 Environment

| Parameter | Value |
|-----------|-------|
| OS | Windows 11 x64 (build 26200) |
| .NET SDK | 10.0.404 |
| .NET Runtimes | 8.0.31, 10.0.15 |
| Rust | 1.99.1 |
| PowerShell | 7.4+ |

### 2.2 Identities

| Component | SHA-256 / Identity |
|-----------|-------------------|
| Commission commit | `b536ff8` (R5-R1) |
| Tree | `bd2de31ccebb002f0c6bd7d6220db493cb622ab6` |
| Corrected reference oracle | Built from patched `api.rs` (`4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`) |
| Dogwood bridge (native) | `94811767dc92488b2f47ef6d6434677c03798eb9d0e7f26fc56f47610131bda5` (ABI 1) |
| CedarSharp native | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` |
| Probe assembly | `PrepProbe.dll` (net8.0/net10.0) |

### 2.3 Results

| TFM | Corpus | Original Reference | Corrected Reference | D3 Stability |
|-----|--------|-------------------|---------------------|--------------|
| `net8.0` | 124 cases | 113 passed, 11 failed (reference defects) | **124 passed, 0 failed** | Stable |
| `net10.0` | 124 cases | 113 passed, 11 failed (reference defects) | **124 passed, 0 failed** | Stable |

**Original reference failures** (11 cases, all reference defects):
- 6 alternate-encoding cases (ED-06 surface)
- 5 malformed-encoding cases (FCN-02)

**Corrected reference**: 0 failures, 0 pending on both TFMs.

**Self-tests**: 19/19 passed on `net8.0`, 19/19 passed on `net10.0`.

---

## 3. Linux x64 / .NET 8 & .NET 10 (PENDING)

### 3.1 Execution procedure

```bash
# On Linux x64 runner (GitHub Actions ubuntu-latest or equivalent)

# 1. Install prerequisites
sudo apt-get update && sudo apt-get install -y \
    dotnet-sdk-8.0 dotnet-sdk-10.0 \
    rustc cargo \
    libclang-dev pkg-config \
    git

# 2. Clone repository at exact commit
git clone https://github.com/jenolaszlo-sketch/dogwood-sharp.git
cd dogwood-sharp
git checkout b536ff8

# 3. Verify tree identity
git rev-parse HEAD^{tree}
# Must equal: bd2de31ccebb002f0c6bd7d6220db493cb622ab6

# 4. Prepare corrected reference oracle
cd docs/reviews/d3-impl-01-r5-r2/evidence/differential/corrected-oracle
WORKROOT="/tmp/r5r2-work"
mkdir -p "$WORKROOT"

# Locate pinned Dogwood checkout (or fetch if not cached)
# The prepare script assumes local Cargo cache; on fresh runner:
cargo fetch --manifest-path /path/to/pinned/dogwood/Cargo.toml  # or use cargo vendor

# Run preparation (bash version)
SOURCE=$(find ~/.cargo/git/checkouts -name "dogwood-*" -type d -exec sh -c '
  for d; do
    if git -C "$d" rev-parse HEAD 2>/dev/null | grep -q "^929f49e7305534ac61ef06cf8e47269c596dc851$"; then
      echo "$d"; exit 0
    fi
  done
' sh {} + | head -1)

if [ -z "$SOURCE" ]; then
  echo "Pinned Dogwood not found; run 'cargo build' in a Dogwood project first"
  exit 1
fi

PATCHED="$WORKROOT/dogwood-patched"
STAGING="$WORKROOT/corrected-oracle"

rsync -a --exclude=target --exclude=.git "$SOURCE/" "$PATCHED/"
git -C "$PATCHED" -c core.autocrlf=false -c core.eol=lf \
  apply -p1 --whitespace=nowarn \
  "$(pwd)/dogwood-typed-context.patch"

# Verify
ACTUAL=$(sha256sum "$PATCHED/dogwood-language/src/api.rs" | cut -d' ' -f1)
EXPECTED="4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3"
[ "$ACTUAL" = "$EXPECTED" ] || { echo "Hash mismatch"; exit 1; }

# Stage build tree
mkdir -p "$STAGING/src"
cp -r src "$STAGING/"
sed "s|__DOGWOOD_PATCHED_PATH__|$PATCHED/dogwood-language|g" \
  Cargo.toml.template > "$STAGING/Cargo.toml"
cp Cargo.lock "$STAGING/"

# Build
cd "$STAGING"
cargo build --locked --release --offline
ORACLE="$STAGING/target/release/dogwoodsharp-corrected-oracle"

# 5. Build managed probe
cd /path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/differential/probe
dotnet restore --locked-mode
dotnet build -c Release --no-restore -f net8.0
dotnet build -c Release --no-restore -f net10.0

# 6. Stage native assets
# The D2 bridge and CedarSharp native must be built for linux-x64
# and placed in probe/bin/Release/<tfm>/runtimes/linux-x64/native/

# 7. Run differential
for TFM in net8.0 net10.0; do
  PROBE="bin/Release/$TFM/PrepProbe.dll"
  FIXTURES="/path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/fixtures.json"
  REGISTER="/path/to/dogwood-sharp/docs/reviews/d3-impl-01-r5-r2/evidence/register-corrected-reference.json"
  OUT="observations-corrected-reference-$TFM.json"
  LIFECYCLE="lifecycle-corrected-reference-$TFM.json"
  SCRATCH="/tmp/r5r2-scratch-$TFM"
  
  dotnet $PROBE differential "$FIXTURES" "$ORACLE" "$REGISTER" "$OUT" "$LIFECYCLE" "$SCRATCH"
done

# 8. Run self-tests
for TFM in net8.0 net10.0; do
  PROBE="bin/Release/$TFM/PrepProbe.dll"
  ORACLE="/path/to/dogwood-sharp/docs/reviews/d3-impl-01/evidence/differential/r4/upstream-oracle/target/release/dogwoodsharp-upstream-oracle"  # Note: original ref also needed
  dotnet $PROBE self-test "$ORACLE" "self-test-$TFM.json"
done
```

### 3.2 Native bridge build for Linux

```bash
# Build D2 bridge for linux-x64
cd /path/to/dogwood-sharp/native/dogwoodsharp-bridge
cargo build --release --target x86_64-unknown-linux-gnu
# Output: target/x86_64-unknown-linux-gnu/release/libdogwoodsharp_bridge.so

# Build CedarSharp native for linux-x64
cd /path/to/cedar-sharp/native
cargo build --release --target x86_64-unknown-linux-gnu
# Output: target/x86_64-unknown-linux-gnu/release/libcedarsharp_native.so
```

### 3.3 Required evidence capture per cell

| Evidence | File |
|----------|------|
| Observations (corrected reference) | `observations-corrected-reference-{net8.0,net10.0}.json` |
| Lifecycle | `lifecycle-corrected-reference-{net8.0,net10.0}.json` |
| Self-test | `self-test-{net8.0,net10.0}.json` |
| Probe build log | `probe-build-{net8.0,net10.0}.log` |
| Oracle build log | `oracle-build.log` |
| Native identities | `native-identities.txt` |
| .NET environment | `dotnet-environment.log` |

---

## 4. macOS ARM64 / .NET 8 & .NET 10 (PENDING)

### 4.1 Execution procedure (analogous to Linux)

```bash
# On macOS ARM64 runner (GitHub Actions macos-latest or equivalent)

# 1. Install prerequisites
brew install dotnet@8 dotnet@10 rust git llvm pkg-config

# 2-8. Same procedure as Linux, with target adjustments:
# - Target: aarch64-apple-darwin
# - Native bridge: libdogwoodsharp_bridge.dylib
# - CedarSharp native: libcedarsharp_native.dylib
# - RID: osx-arm64
```

### 4.2 Native bridge build for macOS ARM64

```bash
# Build D2 bridge for osx-arm64
cd /path/to/dogwood-sharp/native/dogwoodsharp-bridge
cargo build --release --target aarch64-apple-darwin
# Output: target/aarch64-apple-darwin/release/libdogwoodsharp_bridge.dylib

# Build CedarSharp native for osx-arm64
cd /path/to/cedar-sharp/native
cargo build --release --target aarch64-apple-darwin
# Output: target/aarch64-apple-darwin/release/libcedarsharp_native.dylib
```

---

## 5. Complete matrix summary

| Cell | OS | RID | TFM | Status | Evidence Location |
|------|-----|-----|-----|--------|-------------------|
| 1 | Windows | win-x64 | net8.0 | **EXECUTED** | `evidence/observations-corrected-reference-net8.0.json` |
| 2 | Windows | win-x64 | net10.0 | **EXECUTED** | `evidence/observations-corrected-reference-net10.0.json` |
| 3 | Linux | linux-x64 | net8.0 | **PENDING** | Procedure documented above |
| 4 | Linux | linux-x64 | net10.0 | **PENDING** | Procedure documented above |
| 5 | macOS | osx-arm64 | net8.0 | **PENDING** | Procedure documented above |
| 6 | macOS | osx-arm64 | net10.0 | **PENDING** | Procedure documented above |

---

## 6. Package-consumer matrix (must remain green)

The existing 6-cell JIT + NativeAOT package-consumer matrix from R4 (`a244d65`) must remain green. This is a separate obligation from the differential matrix.

| Cell | OS | RID | TFM | JIT | NativeAOT |
|------|-----|-----|-----|-----|-----------|
| 1 | Windows | win-x64 | net8.0 | PASS | PASS |
| 2 | Windows | win-x64 | net10.0 | PASS | PASS |
| 3 | Linux | linux-x64 | net8.0 | PASS | PASS |
| 4 | Linux | linux-x64 | net10.0 | PASS | PASS |
| 5 | macOS | osx-arm64 | net8.0 | PASS | PASS |
| 6 | macOS | osx-arm64 | net10.0 | PASS | PASS |

*Verified at `a244d65` (R4 terminal run). R5-R2 changes do not affect package-consumer paths.*

---

## 7. Blockers for cross-platform execution

| Blocker | Impact | Resolution |
|---------|--------|------------|
| Oracle executable is Windows-only in R5-R1 | Cannot run differential on Linux/macOS without rebuild | Corrected-reference reproducibility procedure (this document) enables cross-platform oracle build |
| D2 bridge / CedarSharp native must be built per platform | Native assets required for probe | Standard `cargo build --target` procedure documented |
| GitHub Actions runners for Linux/macOS | CI capacity | Standard hosted runners available |
| Cargo cache for pinned Dogwood | Fresh runners lack pinned checkout | `cargo fetch` or `cargo vendor` documented |

---

*This matrix record is part of the DOGWOOD-SHARP-D3-IMPL-01-R5-R2 commission evidence
package. Windows cells executed; cross-platform procedure documented for pending cells.*