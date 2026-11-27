# DOGWOOD-SHARP-D3-QUAL-01-Q3 — Independent Final Requalification

**Commission**: DOGWOOD-SHARP-D3-QUAL-01-Q3
**Date**: 27 November 2026
**Reviewer**: independent Q3 requalification (no producer code changes)
**Disposition**: **C. NOT SUPPORTED**
**Machine-readable results**: `findings.json`, `historical-findings.json`,
`harness-corrections.json`, `independent-execution.json`

---

## 1. Executive disposition

**C. NOT SUPPORTED.**

The D3 **behavior** is independently supported: all six mandatory differential
cells execute 124/124, the OQ-01 controls reproduce as documented, the harness
self-tests pass, the D1/D2/D3 test suites pass, and a Windows control using the
shipped native bridge and the published CedarSharp native also passes 124/124.

Qualification nevertheless cannot be granted. Independent verification found
blocking **evidence-integrity and production-equivalence** failures:

| Finding | Blocking defect |
|---------|-----------------|
| Q3-F01 | The R5-R3 evidence manifests do not verify against the binding tree; one covered file was changed after signing. |
| Q3-F02 | The six-cell differential uses a qualification-only D2 bridge built from the patched reference tree, not the shipped D3 bridge. |
| Q3-F03 | The D3 bridge dependency graph is regenerated, not pinned; an independent rebuild resolved 36 different package versions. |
| Q3-F04 | The CedarSharp native is built from an unpinned public source, not the published 1.0.0 native; the package's declared source commit is not in that repository. |
| Q3-F05 | The original OQ-01 Allow-versus-failure case is outside FCN-05 and has no register row. |
| Q3-F10 | Historical R5-R3 tooling and fixtures were modified in place after the R5-R3 record was signed. |

Under section 11 of the commission, an evidence-integrity failure is a
blocking finding. The correct disposition is therefore C, not A or B. This
does not assert an implementation defect; it asserts that the mandatory
qualification evidence is not provenance-bound to the shipped artifacts.

---

## 2. Exact reviewed identities

| Object | Full identity |
|--------|---------------|
| Handoff commit | `aa319955082edd3ea0ff1ad4fa04c7676c7bf5ab` |
| Handoff tree | `7871374758c54dcba9a5d95b922042d25743c3a0` |
| Execution commit | `12a45557eb991482ee4c7b02f26cea13eee1fa4c` |
| Execution tree | `f58951e4c63935826b9630f69f18d32c02c085f9` |
| Producer terminal run | `46005380010` (head `12a4555`, success) |
| Independent differential run | `46027320360` (head `aa31995`, success) |
| Independent regression run | `46027941574` (head `aa31995`; macOS and Linux passed, Windows recorded below) |
| Published `main` | `aa319955082edd3ea0ff1ad4fa04c7676c7bf5ab` (equals origin/main) |

Ancestry: `12a4555` is an ancestor of `aa31995`. The intervening history is
two commits:

1. `f924ef2` — adds the five R5-R4 documents.
2. `aa31995` — changes one line in `SIX-CELL-MATRIX.md` (duration).

---

## 3. Execution-to-handoff reconciliation

`git diff --name-status 12a4555 aa31995` reports exactly five added files:

```
A docs/reviews/d3-impl-01-r5-r4/OQ01-FCN05-DISPOSITION.md
A docs/reviews/d3-impl-01-r5-r4/README.md
A docs/reviews/d3-impl-01-r5-r4/SIX-CELL-MATRIX.md
A docs/reviews/d3-impl-01-r5-r4/fcn-05.json
A docs/reviews/d3-impl-01-r5-r4/six-cell-matrix.json
```

No production code, dependency, fixture, harness or executable file is in the
diff. `[skip ci]` is present on both handoff commits; it does not conceal an
executable change in the handoff itself.

The historical interval is different: 87 commits separate the R5-R3 baseline
`b23f6a9` from the execution `12a4555`. Those commits are the R5-R4
remediation and cross-platform harness campaign. They are not part of the
execution-to-handoff diff, but they include the post-signature fixture change
and probe change recorded in Q3-F01/F10.

---

## 4. Independent six-cell qualification results

| Cell | OS | RID | TFM | Independent source | Result |
|------|----|-----|-----|--------------------|--------|
| 1 | Windows | win-x64 | net8.0 | local execution at `aa31995` | 124/124 |
| 2 | Windows | win-x64 | net10.0 | local execution at `aa31995` | 124/124 |
| 3 | Linux | linux-x64 | net8.0 | run `46027320360`, job `140601963039` | 124/124 |
| 4 | Linux | linux-x64 | net10.0 | run `46027320360`, job `140601962610` | 124/124 |
| 5 | macOS | osx-arm64 | net8.0 | run `46027320360`, job `140601963081` | 124/124 |
| 6 | macOS | osx-arm64 | net10.0 | run `46027320360`, job `140601962962` | 124/124 |

Every cell: 124 cases, 124 passed, 0 failed, 0 pending, 26 families, 0 coverage
errors. Classification totals are identical on all six cells: 84 agreement,
22 approved-difference, 9 fail-closed-equivalence, 9 operation-failure.

The local Windows execution used:

- corrected oracle built from pinned Dogwood `929f49e` with the typed-context
  patch (`api.rs` SHA-256
  `4ff2eb118a08c13eddf1d54ea1cf178db70f1a148bacb6e377333abeb5b67dd3`);
- a locally rebuilt patched D2 bridge (`620dca4a`);
- the **published** CedarSharp 1.0.0 win-x64 native (`77de6976`), not the
  rebuilt native.

A second Windows control replaced the bridge with the shipped win-x64 bridge
from the independent regression run (`4f0bd32a`, committed lock `ec901557`)
and still passed 124/124 on both TFMs, with a semantic projection identical to
the patched-bridge run. This bounds the behavioral effect of Q3-F02 but does
not cure its provenance defect. The producer run produced `78f64c4b` and a
local shipped build produced `94811767` from the same lock, so the Windows
binary is not byte-reproducible.

The hosted cells used the rebuilt CedarSharp native (`9e8800ed` Linux,
`740e7410` macOS) and the patched D2 bridge (`4b6c3409` Linux, `73d29a70`
macOS), not the published or shipped natives.

Raw evidence: `evidence/hosted/`, `evidence/windows/`.

---

## 5. Qualification harness correction assessments

### Q3-H01 — CedarSharp native identity

**Verified with limitations.** The crate's `native/build.rs` writes the
compiler and target into the version response. `build-cedarsharp.sh` requires
active rustc 1.94.0 before building. The hosted identity response is exactly
sdk 4.13.0, language 4.5, bridge 0.1.0, rust 1.94.0, with the RID target. The
C# loader checks the loaded identity, so a manifest-only spoof does not pass.

Limitations: the CedarSharp source is cloned unpinned (Q3-F04); the
wrong-compiler path was verified by source inspection, not a full script run.

### Q3-H02 — Corrected fixture selection

**Verified.** The executed observations record the committed LF blob hash
`568267E0...`; the register hash is `5962C4DC...`. The only content change
between the original and corrected fixtures is `intendedStage:
request-validation` for the five malformed typed-encoding cases. No
authorization outcome was weakened and no unexpected divergence is suppressed.

Limitation: Windows evidence records the CRLF hash `B563AD19...` for the same
JSON content (Q3-F06).

### Q3-H03 — macOS resident native control

**Verified.** The probe loads the resolved path, gets the exported
`dogwoodsharp_abi_version` symbol and calls `dladdr`. Both macOS cells record
`modulePath` equal to the verified asset and `loadedModuleMatchesVerifiedAsset:
true`. The helper returns null on any failure and the caller fails the check;
there is no assumed-path fallback.

### Q3-H04 — Adjudicator corpus

**Verified.** The 57-case corpus is byte-identical between R5-R1 and R5-R2
(SHA-256 `62B654C1...`). All four independent hosted adjudicator outputs are
byte-identical to the committed R5-R2 outcome (`0F878B4B...`). The committed
Cedar 4.11.0 and 4.13.0 outcomes are identical after removing the engine label.

---

## 6. ED-06 retirement verification

- The original ED-06 row is preserved verbatim in the R5 register: two cases,
  approved fields `decision`, `determining`, `cleanAllow`.
- The original reference still produces clean Deny on the two base cases and
  the original 124-case run still records 113/124 with the eleven reference
  defects.
- The corrected reference and D3 agree as clean Allow on the two base cases
  **and** all six alternate encodings on every independent cell.
- The corrected-reference patch changes only
  `dogwood-language/src/api.rs`; the patched file hash matches the recorded
  identity `4ff2eb11...`.
- The representation campaign is not exclusion by altered expectations: the
  cases are ordinary `agreement` expectations, the original expectations
  remain registered, and the direct adjudicator confirms all three JSON
  spellings are semantically identical.
- Measured test counts: 16 `C23TypedContextRepresentationInvarianceTests`
  cases per TFM, 28 `C23TypedContextTests`, 96 D3 tests total and 71 core tests
  per TFM, all passing. The documented "40 representation-invariance tests"
  claim is not the measured xUnit count (Q3-F08).

---

## 7. OQ-01 / FCN-05 independent adjudication

- Seven controls independently re-executed on net8.0 and net10.0 with the
  corrected oracle and D3:
  - TC-01: Allow / Allow (agreement).
  - TC-02: Deny / Deny (agreement).
  - TC-03 through TC-06: reference evaluation failure with no decision; D3
    trusted-binding failure with no decision; stage-only divergence; no
    Allow on either side.
  - TC-07: Deny / Deny (agreement).
- The four divergent controls are fail-closed on both sides. FCN-05's
  classification as a fail-closed normalization is consistent with the
  observed authorization outcomes.
- The original single-case OQ-01 fixture independently reproduces **D3 Allow**
  versus **reference evaluation failure**. FCN-05 explicitly covers TC-03
  through TC-06 only and cannot cover this case. No ED/FCN row registers it;
  only the owner's prose contract decision does (Q3-F05).
- FCN-05's supplemental record is traceable to the committed `6dd3053`
  evidence, and the signed 124-case register is unchanged.

No authoritative-contract contradiction to FCN-05 itself was found. The
contradiction is the unregistered original case, not the four normalized
controls.

---

## 8. Historical findings register

The complete register is `historical-findings.json`. It covers QD3Q-01 through
QD3Q-16, ED-06 and OQ-01, with original requirement, remediation, independent
verification, evidence, final disposition and limitations. No finding was
renamed, omitted or consolidated.

Key outcomes:

- QD3Q-01, 02, 03, 04, 06, 07, 09, 10: verified remediated.
- QD3Q-05: bounded, structurally unreachable; owner acknowledgment required and
  present; literal path unexecuted.
- QD3Q-08: **failed** for R5-R3 (Q3-F01).
- QD3Q-11, 13, 14, 15, 16: documented residual risks/accepted design.
- QD3Q-12: five gaps retain original ratings; C-11 discharged.
- ED-06: retired, preserved.
- OQ-01: FCN-05 formalized for TC-03..TC-06; original case unregistered.

---

## 9. D1, D2 and D3 regression results

Independent regression run `46027941574` (head `aa31995`):

| Platform | Result | Evidence |
|----------|--------|----------|
| macOS osx-arm64 | **PASS** (14m30s) | `evidence/ci/macos/` |
| Linux linux-x64 | **PASS** (16m26s) | `evidence/ci/linux-*` |
| Windows win-x64 | **PASS** (21m11s) | `evidence/ci/windows-*` |

All three jobs executed `eng/Verify.ps1`: native build and
manifest verification, native self-test, D1-on-D2 regression, D2 R2 corpus,
managed tests on net8.0/net10.0, AOT publish and smoke execution, and artifact
integrity. Both report 71/71 core and 96/96 D3 tests, AOT pass, and a shipped
native built from the committed lock `ec901557`.

Local Windows execution additionally ran 71/71 core and 96/96 D3 tests on both
TFMs, the 19/19 harness self-test, AOT-independent differential runs, and the
invalid-ABI regression. No skipped tests.

The same independent run packed both package candidates and executed the
six-cell JIT + NativeAOT package-consumer matrix: **6/6 cells passed**. The
verification report binds the stable package (`681af4a7...`) to the shipped
natives (win `4f0bd32a`, linux `39afbfd0`, mac `fbd2e98a`), the experimental
package (`85f9353e...`) to `DogwoodSharp` + CedarSharp 1.0.0, and the
CedarSharp package to the published natives (win `77de6976`, linux
`85433a35`, mac `290fcce9`). No package was published.

The frozen op-3 and op-4 surfaces are covered by the managed/native test
suites; the execution-to-handoff diff contains no production change. The D3
behavior is preserved within the tested contract.

---

## 10. Evidence integrity verification

| Evidence | Result |
|----------|--------|
| Execution→handoff diff | Five documentation files only; no executable change. |
| R5-R3 top-level manifest (47 entries) | 19 blob matches, 27 CRLF working-tree only, 1 stale. **FAIL**. |
| R5-R3 evidence manifest (31 entries) | 2 blob matches, 27 working-tree only, 1 stale, 1 self-reference. **FAIL**. |
| Independent hosted artifacts | 124/124; fixture and register blob hashes match; adjudicator byte-identical; semantic projections identical to producer runs. |
| Producer/manufactured observations | Not byte-identical because of set ordering and random scratch paths; semantic projections identical. |
| Local Windows evidence | Raw observations, self-test JSON and TRX files included. |
| Reviewer manifest | `manifest.sha256`, computed from committed blobs for every reviewer evidence file. |

The stale R5-R3 entry is `evidence/fixtures.json`: its recorded digest is the
R5-R2 fixtures content with CRLF endings, and commit `44085d3` changed the file
after the R5-R3 manifest was generated. The manifests also hash CRLF
working-tree bytes, so a clean LF checkout cannot verify them.

The native binaries are also not uniformly reproducible: three Windows shipped
builds from the same committed lock produced three different hashes
(`4f0bd32a`, `78f64c4b`, `94811767`), while the Linux and macOS shipped
hashes were stable across the producer and independent runs. The per-artifact
manifest still binds each individual image, but a hash cannot be used as a
cross-environment reproducibility claim.

Raw independent evidence is under `evidence/hosted`, `evidence/windows`,
`evidence/oq01`, `evidence/tests`, `evidence/ci`. Producer evidence remains
under the R5-R1/R5-R2/R5-R3/R5-R4 directories and is not merged into this
reviewer tree.

---

## 11. Qualified support boundaries

The following are supported by the independent execution, subject to the
blocking findings:

- D3 authorization behavior for the 124-case corpus on Windows x64, Linux x64
  and macOS ARM64, net8.0 and net10.0.
- The corrected reference's typed-context correction and ED-06 retirement
  behavior.
- The OQ-01 seven-control semantic result and FCN-05's four-case stage
  normalization.
- The shipped win-x64 D3 bridge and published CedarSharp win-x64 native for
  the same corpus.
- D1/D2 regression and AOT behavior on the platforms whose regression jobs
  passed.

These boundaries do **not** authorize a claim that the hosted Linux/macOS
evidence was produced by the shipped native artifacts; it was not.

---

## 12. Remaining limitations and blockers

**Blocking:**

1. Q3-F01 — R5-R3 manifest verification failure and stale fixture digest.
2. Q3-F02 — differential native is not the shipped native.
3. Q3-F03 — regenerated, unpinned D3 bridge graph (36-package drift observed).
4. Q3-F04 — unpinned CedarSharp source and package-native mismatch.
5. Q3-F05 — original OQ-01 Allow-versus-failure case unregistered.
6. Q3-F10 — post-signature modification of R5-R3 evidence and tooling.

**Non-blocking:**

- Q3-F06 — cross-cell fixture byte hashes differ by line ending.
- Q3-F07 — harness self-test is Windows-only.
- Q3-F08 — representation-test count claim inaccurate.
- Q3-F09 — D2 bridge identity is a hardcoded compatibility value, not
  compiler-attested.
- QD3Q-11, 13, 14, 15, 16 residual risks remain as documented.

---

## 13. Final owner acceptance recommendation

**Do not grant final D3 acceptance at this revision.** The behavior is
independently supported, but acceptance would accept evidence that is not
bound to the shipped artifacts and a historical manifest that does not
verify.

Recommended remediation before a new qualification:

1. Regenerate the R5-R3 (or a superseding) evidence manifest from committed Git
   blobs, not the CRLF working tree, and record the post-R5-R3 fixture change
   with a superseding revision rather than editing the signed record.
2. Build the differential D3 bridge from the shipped `Build-Native.ps1` graph
   (unpatched upstream Dogwood, committed lock) and re-run all six cells; or
   explicitly version the patched differential bridge as a separate artifact
   and qualify the shipped native independently.
3. Commit and use a lock for the D3 differential bridge; record the full lock
   text, not only its hash.
4. Pin the CedarSharp source commit and either use the published 1.0.0 native
   for the cells or record an explicit package-native equivalence campaign.
5. Register the original OQ-01 case as an approved difference (or an explicit
   exclusion) with an owner decision and a machine-checkable row.
6. Correct the representation-test count and the historical-preservation claims.

---

## 14. Release readiness recommendation

**Not release-ready.** Do not publish NuGet packages, create release tags, or
advance the roadmap from this revision. Release readiness is downstream of
owner acceptance, which is not recommended until the blocking findings are
remediated and requalified.

---

*Reviewer evidence only. No production code, dependency, fixture, harness or
historical record was modified by this commission. The only committed changes
are the reviewer records and evidence under
`docs/reviews/d3-qualification-01-q3/`.*
