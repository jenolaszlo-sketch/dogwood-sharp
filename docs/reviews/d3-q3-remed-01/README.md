# DOGWOOD-SHARP-D3-Q3-REMED-01 — Targeted remediation of Q3 blocking findings

**Commission**: DOGWOOD-SHARP-D3-Q3-REMED-01
**Date**: 27 November 2026
**Final disposition**: **B. REMEDIATION INCOMPLETE — BOUNDED BLOCKERS REMAIN**
**Blocking gate**: owner acceptance of the original OQ-01 reference-defect record (RD-01)
**Machine-readable register**: `findings-closure.json`

---

## 1. Final disposition

**B. REMEDIATION INCOMPLETE — BOUNDED BLOCKERS REMAIN.**

Five of the six findings have complete producer remediation evidence and
executions. Q3-F05 has a complete reproducer, semantic analysis, targeted
controls and a proposed supplemental record, but the new reference-defect
classification **RD-01 is not binding without owner acceptance**. Under the
commission, A cannot be selected while that owner decision is outstanding.

No production code was changed. The changes are evidence tooling, build
provenance, qualification-harness isolation, CI and documentation.

---

## 2. Baseline and successor identities

| Object | Identity |
|--------|----------|
| Independent Q3 reviewer commit | `e980ee2e91d96b314b1564a9794725fa7214fb33` |
| Independent Q3 reviewer tree | `d902e50011691b5b689b86fe7c13d7dffb173573` |
| R5-R4 handoff | `aa319955082edd3ea0ff1ad4fa04c7676c7bf5ab` / `7871374758c54dcba9a5d95b922042d25743c3a0` |
| R5-R4 execution | `12a45557eb991482ee4c7b02f26cea13eee1fa4c` / `f58951e4c63935826b9630f69f18d32c02c085f9` |
| Producer terminal run | `46005380010` |
| Independent differential run | `46027320360` |
| Independent regression run | `46027941574` |
| Remediation evidence-verification run | `46033663477` |
| Shipped-subject campaign commit / run | `2366f7f` / `46039052322` |

The 71-entry Q3 reviewer manifest was independently re-verified at
`e980ee2`: **71/71** committed blobs.

---

## 3. Six-finding remediation matrix

| Finding | Requirement | Reproduced | Remediation identity | Verification | Producer recommendation | Owner decision |
|---------|-------------|------------|----------------------|--------------|-------------------------|----------------|
| Q3-F01 | manifests verify against the binding tree | 19 blob / 27 CRLF / 1 stale at HEAD; never valid on LF | canonical hashing contract, successor manifests, verifier | Q3 71/71; b23f6a9 47/47; HEAD 57/57; self-test 7/7 on Windows + Ubuntu | close | none |
| Q3-F02 | actual shipped product native under test | prior cells used patched-reference bridge | shipped-subject workflow using `Build-Native.ps1` + published CedarSharp native | six-cell campaign, resident identity, negative control | close after reviewer confirmation | none |
| Q3-F03 | locked graph and reproducibility | 36-package regeneration; Windows metadata variance | shipped lock `ec901557` used with `--locked`; unshipped path retired | lock identity, 24-byte metadata-only diff, normalized equality | close for shipped candidate | none |
| Q3-F04 | published CedarSharp provenance | unpinned clone; package-native mismatch | published package `99cc3659` and per-RID natives used; clone retired | nuspec commit `68ffdafa`; hash-bound natives; package consumer 6/6 | close with limitation | none unless reviewer treats the external metadata inconsistency as blocking |
| Q3-F05 | original OQ-01 adjudication and register | D3 Allow vs reference failure reproduced on both TFMs | RD-01 proposal + supplemental register | seven controls, original case, current-invalid negative | close after owner acceptance | **accept RD-01** |
| Q3-F10 | historical preservation | 9 added harness files, 1 modified fixture, probe modified | append-only correction; original + successor identities | canonical manifests and change list | close | none |

Details: `findings-closure.json`, `WS-A-EVIDENCE-INTEGRITY.md`,
`WS-C-DEPENDENCY-PROVENANCE.md`, `OQ01-SEMANTIC-ADJUDICATION.md`.

---

## 4. Original OQ-01 semantic adjudication

**Proposed disposition S-A: DogwoodSharp is correct; the reference oracle is
defective.**

The original case was reproduced with the shipped win-x64 bridge
(`78f64c4b`) and the published CedarSharp native (`77de6976`) on net8.0 and
net10.0:

- history: one `Approve` event, no entities, stock `AMZN`;
- current: valid `Sell` request with `item-1.owner = alice`;
- reference: evaluation-stage failure, no decision;
- DogwoodSharp: evaluation-stage Allow.

The reference oracle calls `is_authorized` on the history event and treats a
policy error there as fatal. The current request is valid, the temporal leaf
resolves from the trace, and the forbid applies to `Approve`, not the current
`Sell`. The contract replays history as a trace and authorizes only the
current event.

Targeted controls:

- TC-01 / TC-02 / TC-07 agree (Allow / Deny / Deny).
- TC-03 through TC-06 fail closed on both sides (FCN-05 scope, unchanged).
- A new current-invalid control fails closed on DogwoodSharp, proving the
  original Allow is not a general continuation-after-invalid-replay behavior.
- Provider and malformed-input controls remain separate.

**RD-01** is proposed as a one-case reference-defect record. It is recorded in
`evidence/oq01-supplemental-register.json` as
`proposed-pending-owner-acceptance`. The signed 124-case register and FCN-05
are unchanged.

**Owner gate**: accept RD-01 (or direct a different reference-oracle
disposition). Until then Q3-F05 remains a bounded blocker.

---

## 5. Dependency graph and lock identities

The shipped product bridge is built by `eng/Build-Native.ps1` from
`native/dogwoodsharp-bridge` with the upstream `dogwood-language` git revision
`929f49e`, Cedar 4.11.0 and the committed lock
`ec9015575a33925d15033a5fff763b660427ad6ef52e762b2ff65d4dded1e2aa`
(`cargo build --locked`).

The prior qualification-only path (`scripts/build-bridge.sh` with the patched
reference path) regenerated the lock and resolved 36 different packages. That
path is retained only as the negative-control wrong subject. The full
divergence and root cause are in `WS-C-DEPENDENCY-PROVENANCE.md` and
`evidence/dependency-graph.json`.

---

## 6. CedarSharp provenance disposition

The binding dependency is the published **CedarSharp 1.0.0** package
(`99cc3659e750f3d5187d7b8c35e84359811a7af1247b3673d893eafc679bf835`). Its
nuspec records repository commit
`68ffdafa3bd9a2e4e5744069220bededb5684c5b` (tag `v1.0.0`), which is
recoverable.

The package's native manifests declare `sourceCommit
324d3c09fc94ab91464ec340eb81e4b167deb6f5`; that commit is not present in the
repository (HTTP 422) and is inconsistent with the nuspec. The published
per-RID natives are used as immutable artifacts:

| RID | Published native SHA-256 |
|-----|--------------------------|
| win-x64 | `77de6976a7ae5b961920bb6843d39b32f3faf285f16b723581680e9c06c03895` |
| linux-x64 | `85433a352ed9b6b0122e77a4e37cbe9ba1354872641e217350bdf871ea33ebf6` |
| osx-arm64 | `290fcce995f70cd963e024d991fe7f9e2806fde81add9749304ce222b904258b` |

The unpinned `main` clone and rebuilt natives are retired from qualification.
The external package's `sourceCommit` inconsistency remains a documented
provenance limitation; the nuspec commit, package hash, native hashes and
native lock hash are the binding identities.

---

## 7. Binary reproducibility investigation

Two Windows shipped builds from the same source and lock
(`78f64c4b` and `4f0bd32a`, both 17,645,056 bytes) differ in exactly 24 bytes:
the PE timestamp, three debug-directory timestamps and the PDB GUID. Zeroing
those metadata fields makes the images byte-identical
(`28add84fff1a7b8fc045c0e593969602264bb7908588f80ea6ae17f845622e9e`).
The code and data are identical; only build metadata differs.

The contract requires a locked graph and exact candidate identity, not
cross-environment byte equality. The shipped campaign qualifies the exact
CI-built candidate and binds it in the manifest. The local older build
(`94811767`, different size) is historical and outside the comparison.

---

## 8. Actual shipped-subject six-cell matrix

Workflow: `D3 Q3 Remediation Shipped-Subject Differential`
(run `46039052322`, commit `2366f7f`).

Each cell uses:

- corrected reference oracle from the patched pinned Dogwood tree;
- product D2 bridge from `eng/Build-Native.ps1` and lock `ec901557`;
- published CedarSharp 1.0.0 native for the RID;
- corrected-reference fixtures and register;
- resident image hash check against the staged product bridge;
- qualification-only bridge substitution negative control.

| Cell | OS / RID / TFM | Cases | Product bridge SHA-256 | CedarSharp native SHA-256 | Resident | Negative |
|------|----------------|-------|------------------------|---------------------------|----------|----------|
| 1 | Windows / win-x64 / net8.0 | 124/124 | `b876d1e2...` | `77de6976...` | pass | pass |
| 2 | Windows / win-x64 / net10.0 | 124/124 | `b2e5eb83...` | `77de6976...` | pass | pass |
| 3 | Linux / linux-x64 / net8.0 | 124/124 | `39afbfd0...` | `85433a35...` | pass | pass |
| 4 | Linux / linux-x64 / net10.0 | 124/124 | `39afbfd0...` | `85433a35...` | pass | pass |
| 5 | macOS / osx-arm64 / net8.0 | 124/124 | `fbd2e98a...` | `290fcce9...` | pass | pass |
| 6 | macOS / osx-arm64 / net10.0 | 124/124 | `fbd2e98a...` | `290fcce9...` | pass | pass |

Every cell: 26/26 families, 0 failed, 0 pending, 0 coverage errors, identical
classification totals (84 agreement, 22 approved-difference, 9
fail-closed-equivalence, 9 operation-failure). The product bridge manifest
uses the committed lock `ec901557`. The two Windows per-TFM builds differ only
in embedded build metadata; each is bound by its own manifest and resident
hash. The negative control (qualification-only patched bridge under the
product manifest) is rejected with
`Native asset SHA-256 differs from the manifest.` in all six cells.

Corrected-reference identities per cell are in
`evidence/shipped-campaign/<rid>/<tfm>/reference-assets.json`: oracle hashes
`58c4760b`/`b0ea24b7` (Windows net8/net10), `59cb6688` (Linux), `a20f1dfe`
(macOS), patched `api.rs` `4ff2eb11`, and the corrected-oracle lock hash.
The raw observations, lifecycle JSON, console logs, product-assets JSON and
negative-control logs are in the same directory. The consolidated verifier
result is `evidence/shipped-campaign/results.json` (6/6 pass).

Raw evidence: `evidence/shipped-campaign/`.

---

## 9. Regression impact assessment

No production, dependency, fixture, public API or ABI change is present in
this remediation. The regression applicability rationale is:

- The independent regression run `46027941574` at `aa31995` passed Windows,
  Linux and macOS `eng/Verify.ps1`, including native self-test, D1-on-D2,
  D2 R2 corpus, 71/71 core and 96/96 D3 tests per TFM, AOT publish/smoke, and
  the six-cell JIT/NativeAOT package-consumer matrix (6/6).
- The shipped campaign re-executes the 124-case differential against the
  actual product bridge and published CedarSharp natives.
- The remediation changes are qualification harness, build provenance and
  documentation only; they do not alter production behavior or dependencies.

No unaffected result is used to mask a changed subject.

---

## 10. Evidence integrity verification

- Q3 reviewer manifest at `e980ee2`: **71/71**.
- Canonical R5-R3 manifest at `b23f6a9`: **47/47**.
- Canonical R5-R3 manifest at HEAD: **57/57**.
- Manifest verifier self-test: **7/7** on Windows 2025 and Ubuntu 24.04
  (run `46033663477`).
- The controls detect changed content, missing artifacts, stale checksums and
  CRLF-only matches; newline normalization never passes silently.
- The original R5-R3 manifest is preserved; the successor manifests are new
  records, not rewritten signatures.
- The remediation evidence manifest is `manifest.sha256` in this directory,
  computed over committed Git blobs.

---

## 11. Outstanding owner decisions

**RD-01 acceptance.** The owner must accept, reject or redirect the proposed
one-case reference-defect classification for
`R5R1-OQ-01-temporal-history-event-policy-evaluation-error`. Until then:

- Q3-F05 is not closed;
- the final disposition is B;
- no D3 acceptance, release or publication is authorized.

No other owner decision is outstanding. The CedarSharp external metadata
inconsistency is documented; the reviewer may elevate it if the binding
contract requires a recoverable native source commit.

---

## 12. Independent Q3-R1 readiness recommendation

The evidence, provenance and shipped-subject workstreams are ready for an
independent **Q3-R1 delta requalification**. The delta reviewer should:

1. verify the original and successor manifest identities and the 7/7 controls;
2. verify the shipped campaign subject, resident image, negative control and
   six cell results;
3. verify the committed lock and published CedarSharp hashes;
4. adjudicate the RD-01 proposal or record the owner's decision;
5. preserve the Q3 reviewer records and this remediation evidence separately.

Q3-R1 cannot complete the semantic gate until the owner decides RD-01. This
handoff does not declare D3 qualified, grant acceptance, create release tags
or publish packages.
