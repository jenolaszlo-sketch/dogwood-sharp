# WS-C — Dependency identity and reproducibility (Q3-F03, Q3-F04)

**Commission**: DOGWOOD-SHARP-D3-Q3-REMED-01
**Date**: 27 November 2026
**Status**: shipped graph pinned; provenance limitations recorded

---

## 1. Shipped D3 bridge graph (Q3-F03)

| Field | Identity |
|-------|----------|
| Builder | `eng/Build-Native.ps1` |
| Source | `native/dogwoodsharp-bridge` |
| Upstream dependency | `dogwood-language` git `929f49e7305534ac61ef06cf8e47269c596dc851` |
| Cedar | `cedar-policy-core` `4.11.0` |
| Lock | `native/dogwoodsharp-bridge/Cargo.lock` |
| Lock SHA-256 | `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa` |
| Resolution | `cargo build --locked` |
| Rust | 1.99.1 |
| Profile | release, panic=unwind |
| Status | **pinned** |

The shipped-subject campaign (`D3 Q3 Remediation Shipped-Subject Differential`)
builds this graph for every cell. It does not use the qualification-only
patched bridge.

## 2. The 36-package divergence

The qualification-only differential bridge used `scripts/build-bridge.sh`,
which rewrites `dogwood-language` to the patched reference path and runs
`cargo generate-lockfile`. Only `cedar-policy-core` is pinned afterwards. An
independent rebuild resolved 36 packages differently from the committed lock:

| Package | Committed | Independent rebuild |
|---------|-----------|---------------------|
| jiff | 0.2.46 | 1.0.3 |
| jiff-core | 0.1.8 | 1.0.2 |
| jiff-static | 0.2.46 | 1.0.3 |
| regex | 1.13.1 | 1.13.2 |
| regex-automata | 0.4.18 | 0.4.19 |
| serde_with | 3.24.0 | 3.25.0 |
| uuid | 1.29.2 | 1.29.3 |
| zerocopy | 0.8.63 | 0.8.66 |
| toml_edit | 0.25.19+spec-1.1.0 | 0.25.21+spec-1.1.0 |
| pest | 2.9.6 | 2.9.7 |

The complete list is `evidence/dependency-graph.json`. The root cause is an
unlocked resolution after the path rewrite, not a feature/target change.
Impact is confined to the **unshipped** differential bridge; the shipped graph
uses the committed lock. That path is retained only as the negative-control
subject.

## 3. Windows binary reproducibility

Three Windows builds from the same committed lock produced:

| Build | SHA-256 | Size | PE timestamp |
|-------|---------|------|--------------|
| Producer CI `46005379891` | `78f64c4b...` | 17,645,056 | 2026-11-27T13:20:01Z |
| Independent CI `46027941574` | `4f0bd32a...` | 17,645,056 | 2026-11-27T15:52:29Z |
| Older local build | `94811767...` | 17,643,008 | 2026-11-22T18:40:21Z |

The two same-source/same-lock CI builds differ in exactly **24 bytes**:

- 4-byte PE TimeDateStamp (`0x108`);
- three 4-byte debug-directory timestamps (`0xD9DA74`, `0xD9DA90`, `0xD9DAAC`);
- the 16-byte CodeView/PDB GUID (`0xD9DBB8`).

Zeroing those metadata fields makes the two images **byte-identical**
(normalized SHA-256 `28add84fff1a7b8fc045c0e593969602264bb7908588f80ea6ae17f845622e9e`).
The executable code and data are identical; the difference is embedded build
metadata. The older local build has a different size and is outside the
same-source comparison.

The contract requires a locked graph and the exact identity of the
distribution candidate, not cross-environment byte equality. Remediation
therefore qualifies the exact CI-built candidate and binds it in the manifest;
it does not assert that independent builds produce the same SHA-256. The
guarantee levels are recorded in `evidence/binary-reproducibility.json`.

## 4. CedarSharp provenance (Q3-F04)

| Field | Identity |
|-------|----------|
| Binding dependency | CedarSharp 1.0.0 |
| Package SHA-256 | `99cc3659e750f3d5187d7b8c35e84359811a7af1247b3673d893eafc679bf835` |
| Package nuspec repository | `https://github.com/jenolaszlo-sketch/cedar-sharp.git` |
| Nuspec commit | `68ffdafa3bd9a2e4e5744069220bededb5684c5b` (tag `v1.0.0`, recoverable) |
| Native lock SHA-256 | `38f989f82af269e77ff41d80aa22560661070a7b0372dc73c8e27f2e3b71f962` |
| Published win-x64 native | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` |
| Published linux-x64 native | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` |
| Published osx-arm64 native | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` |

The native manifest's `sourceCommit` field is
`324d3c09fc94ab91464ec340eb81e4b167deb6f5`; the GitHub API returns HTTP 422
for that commit in the package's repository. The package's own nuspec provides
the recoverable commit `68ffdafa` (tag `v1.0.0`). The native field is
inconsistent with the package metadata and is not used as the binding source
identity.

The rebuilt natives (`9e8800ed` Linux, `740e7410` macOS) come from an unpinned
`main` clone and a regenerated lock; they are **not** the published package
natives. The remediation retires that clone from qualification and uses the
exact published package and per-RID natives for every shipped cell.

Provenance limitation: the external package's native `sourceCommit` cannot be
recovered. The package hash, nuspec commit, native hashes and native lock hash
are the binding identities. This is a limitation of the external dependency,
not a substitution of a rebuilt native.

## 5. Negative controls

- The shipped campaign stages the product bridge manifest and verifies its
  `sha256`, `rid`, `target`, lock hash and identity fields before execution.
- The resident control (`R3-C26-05-resident`) records the loaded image path and
  SHA-256; the campaign asserts it equals the staged product bridge hash.
- The negative control replaces the product bridge with the
  qualification-only patched bridge while keeping the product manifest; the
  loader must reject the hash mismatch before any authorization.

## 6. Evidence

- `evidence/dependency-graph.json`
- `evidence/binary-reproducibility.json`
- `evidence/cedarsharp-provenance.json`
- Shipped campaign artifacts (`r5r3-shipped-*`)
