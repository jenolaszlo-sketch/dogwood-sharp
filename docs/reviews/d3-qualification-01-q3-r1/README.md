# DOGWOOD-SHARP-D3-QUAL-01-Q3-R1 — Independent delta requalification

**Commission**: DOGWOOD-SHARP-D3-QUAL-01-Q3-R1
**Date**: 27 November 2026
**Disposition**: **B. SUPPORTED WITH QUALIFICATIONS**
**Machine-readable findings**: `findings.json`
**Owner decision**: `OWNER-DECISION-RD01.md`

---

## 1. Disposition

**B. SUPPORTED WITH QUALIFICATIONS.**

All six Q3 findings are independently closed for the bound subject:

- Q3-F01 evidence integrity: closed.
- Q3-F02 shipped-subject qualification: closed.
- Q3-F03 dependency provenance: closed; exact candidate identity bound.
- Q3-F04 CedarSharp provenance: closed as a **bounded external qualification**.
- Q3-F05 OQ-01/RD-01: closed by the owner decision and independent controls.
- Q3-F10 historical preservation: closed via append-only successor records.

Two residual qualifications remain explicit and do not block the delta:

1. **CedarSharp external native source metadata.** The published 1.0.0 package
   and all three native images are hash-verified, and the package nuspec
   source commit `68ffdafa` is recoverable. The native manifests declare
   `sourceCommit 324d3c0`, which is not present in the repository. The exact
   native image is bound by its own hash, not by a reproducible source build
   of that declared commit.
2. **Windows native byte identity.** Windows builds from the same source and
   lock differ only in embedded PE/debug timestamps and the PDB GUID; after
   zeroing those fields the images are identical. The contract binds the exact
   per-candidate hash, not cross-environment byte equality.

The remediation record's F04 supporting-evidence path points to the Q3
reviewer tree rather than the remediation tree; the content is present,
hash-verified and cited below. This is a documentation-reference defect, not
an evidence-integrity failure.

---

## 2. Reviewed identities

| Object | Full identity |
|--------|---------------|
| Q3 reviewer baseline | `e980ee2e91d96b314b1564a9794725fa7214fb33` / tree `d902e50011691b5b689b86fe7c13d7dffb173573` |
| Remediation record | `d2d9ad72e129a04e9d3502a3382103240aaa1c94` / tree `1692b33d0fff5648dcabc23ddd80e173512ad76d` |
| Q3 reviewer manifest | 71/71 |
| Remediation manifest | 74/74 |
| Producer shipped campaign | `46039052322`, commit `2366f7ff39eaba6a03afc229939a4f67ec2be6eb` |
| Independent evidence campaign | `46044342439`, commit `d2d9ad7` |
| Independent shipped campaign | `46044345656`, commit `d2d9ad7` |
| Cross-platform verifier campaign | `46033663477`, commit `0894b209477ca7fc63eb57a1366590e485acd075` |

`origin/main` equals the reviewed remediation head at review start. No
substitution of revision, dependency, native asset or subject was found.

---

## 3. Q3-F01 — Evidence integrity

Independently re-executed `D3 Q3 Remediation Evidence Verification` at the
remediation head (`46044342439`), on Windows 2025 and Ubuntu 24.04:

| Check | Windows | Linux |
|-------|---------|-------|
| Q3 reviewer manifest | 71/71 | 71/71 |
| R5-R3 canonical at `b23f6a9` | 47/47 | 47/47 |
| R5-R3 canonical at HEAD | 57/57 | 57/57 |
| Manifest self-test | 7/7 | 7/7 |

The self-test exercises exact pass, changed content, missing artifact, stale
checksum, CRLF-vs-LF mismatch, the Q3 manifest, and a stale copy. Newline
normalization never passes silently. The remediation manifest verifies
**74/74** against committed blobs.

**Disposition: closed.**

---

## 4. Q3-F02 — Shipped-subject qualification

Independent run **`46044345656`** at the remediation head (`d2d9ad7`) produced:

| Cell | Cases | Product bridge SHA-256 | CedarSharp native SHA-256 | Resident | Negative |
|------|-------|------------------------|---------------------------|----------|----------|
| Windows / win-x64 / net8.0 | 124/124 | `c607e815f489502e695471e14a6c1b8a3740029f2173151ace019b341c08d016` | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` | pass | pass |
| Windows / win-x64 / net10.0 | 124/124 | `6345c7b2cbedf0233c761cb3ace0ca73940e16052ce5427e0ec669d62ec66ec9` | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` | pass | pass |
| Linux / linux-x64 / net8.0 | 124/124 | `39afbfd0b8ed60d627af6e3e0e743c594e8d72817fee16f70d98a39081dafbe9` | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` | pass | pass |
| Linux / linux-x64 / net10.0 | 124/124 | `39afbfd0b8ed60d627af6e3e0e743c594e8d72817fee16f70d98a39081dafbe9` | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` | pass | pass |
| macOS / osx-arm64 / net8.0 | 124/124 | `fbd2e98ac6dc393b3afb42bc9684f3843f75f030107ce72cae675469c17db42e` | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` | pass | pass |
| macOS / osx-arm64 / net10.0 | 124/124 | `fbd2e98ac6dc393b3afb42bc9684f3843f75f030107ce72cae675469c17db42e` | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` | pass | pass |

Every cell: 26/26 families, 0 failed, 0 pending, 0 coverage errors, identical
classification totals. The Windows per-TFM product bridge hashes differ only
in embedded build metadata; each is bound by its manifest and resident hash.
Fresh reference identities are in each cell's `reference-assets.json`:
`15939762`/`8a628ffa` (Windows net8/net10), `59cb6688` (Linux), `a20f1dfe`
(macOS), patched `api.rs` `4ff2eb11`, corrected-oracle lock hash recorded.

Each cell used the product D2 bridge from `eng/Build-Native.ps1` with the
committed lock `ec901557...`, the published CedarSharp 1.0.0 native for the
RID, the corrected reference oracle from the patched pinned Dogwood tree, and
the corrected-reference fixtures/register. The resident control recorded the
loaded path and hash, and the campaign asserted equality with the staged
product bridge. The negative control substituted the qualification-only
patched bridge under the product manifest; every cell rejected it with
`Native asset SHA-256 differs from the manifest.` before authorization.

The same subject identities were independently reproduced in the producer
campaign `46039052322` and the reviewer campaign `46044345656`. No
qualification-only implementation was substituted for the shipped subject.
Both campaigns agree on every one of the 124 classifications; Linux and macOS
product-bridge hashes are identical across the two runs, while the Windows
per-build hashes differ only in embedded metadata. The comparison is
`evidence/shipped-campaign/campaign-comparison.json`.

**Disposition: closed.**

---

## 5. Q3-F03 — Dependency provenance

- The shipped product graph is `eng/Build-Native.ps1` with
  `native/dogwoodsharp-bridge/Cargo.lock`, SHA-256
  `ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa`,
  built with `cargo build --locked`.
- All six shipped-cell `product-assets.json` records that exact lock.
- The retired qualification-only path regenerated its lock and resolved 36
  different packages; the divergence is independently reproduced from the
  retained regenerated lock.
- The unshipped path is used only as the negative-control wrong subject.
- The Windows producer/independent images differ in 24 bytes (PE timestamp,
  three debug-directory timestamps, PDB GUID); after zeroing those fields both
  images hash to
  `28add84fff1a7b8fc045c0e593969602264bb7908588f80ea6ae17f845622e9e`.
  Code and data are identical.

The contract requires locked resolution and exact candidate identity, not
cross-environment byte equality. The exact per-cell manifest hash is the
binding artifact identity.

**Disposition: closed, with the byte-identity qualification in section 1.**

---

## 6. Q3-F04 — CedarSharp provenance

Independent verification:

- Package `CedarSharp 1.0.0` SHA-256
  `99cc3659e750f3d5187d7b8c35e84359811a7af1247b3673d893eafc679bf835`.
- Nuspec repository commit
  `68ffdafa3bd9a2e4e5744069220bededb5684c5b` (tag `v1.0.0`, recoverable;
  subject "Record CedarSharp 1.0.0 qualification evidence").
- Published natives:
  `77de6976...` (win-x64), `85433a35...` (linux-x64), `290fcce9...`
  (osx-arm64), each used by the shipped campaign.
- Package consumer matrix: 6/6 JIT + NativeAOT cells passed
  (`dogwoodsharp-d3-package-verification` and the independent regression run
  `46027941574`).
- The native manifests declare `sourceCommit 324d3c0`; the GitHub API returns
  HTTP 422 for that commit in the package repository. The nuspec commit is
  recoverable, but the native's declared source commit is not.

**Independent judgment:** this is a **bounded external provenance
qualification**, not a blocking defect. The binding consumer artifact is the
published package; its package hash, nuspec commit, native hashes and native
lock hash are immutable and independently verified. The inconsistent native
metadata does not alter the executed image identity or behavior. If the
binding contract were to require source reproducibility of the external
native from `324d3c0`, the limitation would become blocking; the D3
qualification contract pins CedarSharp 1.0.0 as a candidate, not a source
build.

**Disposition: closed as a bounded qualification.**

---

## 7. Q3-F05 — OQ-01 and RD-01

The owner decision `OWNER-DECISION-RD01.md` accepts S-A and RD-01 within the
documented OQ-01 scope. Independent re-execution with the shipped win-x64
bridge `78f64c4b` and published CedarSharp native `77de6976`:

- Original OQ-01: reference evaluation failure/no decision; DogwoodSharp
  Allow.
- TC-01: Allow/Allow.
- TC-02: Deny/Deny.
- TC-03..TC-06: reference evaluation failure/no decision; D3 trusted-binding
  failure/no decision. FCN-05 remains exactly these four controls.
- TC-07: Deny/Deny.
- New current-invalid control: D3 fails closed; no Allow.

The signed 124-case register and corrected fixture are unchanged at the
remediation head (SHA-256 `5962c4dc...` and `568267e0...`). FCN-05 is not in
that signed register and has not been expanded. RD-01 is a separate
reference-defect record, not an FCN or an approved authorization difference.

RD-01 accurately describes the reference-oracle defect. DogwoodSharp's Allow
is contract-correct. The current-invalid control fails closed, so RD-01 does
not conceal a general continuation-after-invalid-replay defect. Historical
evidence validation remains effective for present history entities
(TC-03..TC-06).

**Disposition: closed.**

---

## 8. Q3-F10 — Historical evidence preservation

- `e980ee2..d2d9ad7` changes **zero** files under
  `docs/reviews/d3-impl-01-r5-r3/`.
- Original R5-R3 top-level and evidence manifests are byte-identical at both
  commits; the original fixtures file is unchanged.
- The post-signature change record lists nine added harness files, one
  modified fixture (`44085d3`) and the later probe modification (`17d90b8`).
- Successor blob manifests verify at `b23f6a9` (47/47) and HEAD (57/57).
- The original mixed-convention signature is preserved and is not claimed
  valid for later content or LF checkouts; the canonical contract and
  successor records are explicit.

**Disposition: closed.**

---

## 9. Residual qualifications

1. **CedarSharp native source metadata** (bounded, accepted). The published
   package and native images are hash-verified; the native `sourceCommit
   324d3c0` is unrecoverable and inconsistent with the nuspec commit
   `68ffdafa`. The package is the binding artifact.
2. **Windows byte identity** (bounded). Same-source/lock Windows images differ
   only in embedded timestamps/PDB GUID; the exact candidate hash is binding.
3. **Documentation reference** (non-blocking). The remediation `findings-
   closure.json` F04 path points to `evidence/ci/package-verification-
   report.json` in the remediation tree; the file is in the Q3 reviewer tree.
   The content is verified and cited here.

No residual qualification waives a mandatory execution or provenance
requirement.

---

## 10. Authority exclusions

This Q3-R1 record does not grant D3 qualification, formal closure, release
authorization, release tagging, NuGet publication or any later development
stage. Those remain separately gated.
